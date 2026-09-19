using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>
    /// 多 Sheet 与分章节生成测试（对齐 Idle 约定：sheet 名 前缀_数字 → 聚合 Getter 按章节查询）。
    ///
    /// 0.6.0 起一个章节族共用包内一个目录 <c>&lt;包&gt;/&lt;前缀&gt;/</c>：
    /// 代码在 <c>Code/</c>（基类 + 各章节子类 + 章节 Getter + 聚合 Getter），
    /// 数据在 <c>Data/&lt;前缀&gt;_&lt;章节&gt;.cbcfg</c>（数据名 = sheet 名）。
    /// </summary>
    public class CExcelMultiSheetTests
    {
        private string _tmpXlsx;
        private string _tmpOut;

        [SetUp]
        public void SetUp()
        {
            _tmpXlsx = CExcelTestFactory.CreateMultiSheetTempTable(
                ("ChapterConfig_1", new[]
                {
                    CExcelTestFactory.Row("Id_i", 1, "Name_s", "第一章", "Rewards_ia", "10;20"),
                    CExcelTestFactory.Row("Id_i", 2, "Name_s", "第一章Boss", "Rewards_ia", "30"),
                }),
                ("ChapterConfig_2", new[]
                {
                    CExcelTestFactory.Row("Id_i", 1, "Name_s", "第二章", "Rewards_ia", "50"),
                }));
            _tmpOut = Path.Combine(Path.GetTempPath(), "coffeebean_chapter_out_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            CExcelTestFactory.DeleteTempFile(_tmpXlsx);
            if (Directory.Exists(_tmpOut)) Directory.Delete(_tmpOut, true);
        }

        [Test]
        public void GenerateAllSheets_ProducesChapterArtifacts()
        {
            CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut, "Config");
            CExcelGenerateResult result = CExcelGenerator.GenerateAllSheets(_tmpXlsx, options);

            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            // 每章节：数据容器 + 子类 + 章节 Getter；聚合：基类 + 聚合 Getter —— 全在 <包>/ChapterConfig/ 下
            Assert.IsTrue(File.Exists(CExcelTestFactory.DataPath(options, "ChapterConfig", "ChapterConfig_1")));
            Assert.IsTrue(File.Exists(CExcelTestFactory.DataPath(options, "ChapterConfig", "ChapterConfig_2")));
            Assert.IsTrue(File.Exists(CExcelTestFactory.CodePath(options, "ChapterConfig", "ChapterConfigBase.cs")), "应生成章节基类");
            Assert.IsTrue(File.Exists(CExcelTestFactory.CodePath(options, "ChapterConfig", "ChapterConfigChapter1.cs")), "应生成第 1 章子类");
            Assert.IsTrue(File.Exists(CExcelTestFactory.CodePath(options, "ChapterConfig", "ChapterConfigChapter2.cs")), "应生成第 2 章子类");
            Assert.IsTrue(File.Exists(CExcelTestFactory.CodePath(options, "ChapterConfig", "ChapterConfigChapter1Getter.cs")), "应生成第 1 章独立 Getter");
            Assert.IsTrue(File.Exists(CExcelTestFactory.CodePath(options, "ChapterConfig", "ChapterConfigChapter2Getter.cs")), "应生成第 2 章独立 Getter");
            Assert.IsTrue(File.Exists(CExcelTestFactory.CodePath(options, "ChapterConfig", "ChapterConfigGetter.cs")), "应生成聚合 Getter");

            // 旧命名（<前缀>ConfigBase.cs / <前缀>_<N>Config.cs / <前缀>_<N>Getter.cs）必须彻底消失，
            // 否则新老两套产物会同时被 Unity 编译 → 类型重复
            Assert.IsFalse(File.Exists(CExcelTestFactory.CodePath(options, "ChapterConfig", "ChapterConfigConfigBase.cs")),
                "旧名 <前缀>ConfigBase.cs 不该再产出");
            Assert.IsFalse(File.Exists(CExcelTestFactory.CodePath(options, "ChapterConfig", "ChapterConfig_1Config.cs")),
                "旧名 <前缀>_<N>Config.cs 不该再产出");
            Assert.IsFalse(File.Exists(CExcelTestFactory.CodePath(options, "ChapterConfig", "ChapterConfig_1Getter.cs")),
                "旧名 <前缀>_<N>Getter.cs 不该再产出");

            // 数据容器能按运行时的方式解开（章节数据名 = sheet 名）
            StringAssert.Contains("第一章", CExcelTestFactory.ReadDataJson(
                CExcelTestFactory.DataPath(options, "ChapterConfig", "ChapterConfig_1")));
        }

        [Test]
        public void ChapterGetter_ProvidesChapterQueries()
        {
            CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut);
            CExcelGenerator.GenerateAllSheets(_tmpXlsx, options);
            string getter = File.ReadAllText(CExcelTestFactory.CodePath(options, "ChapterConfig", "ChapterConfigGetter.cs"));

            StringAssert.Contains("private const string DataPathPrefix = \"ChapterConfig/Data/ChapterConfig_\";", getter,
                "聚合 Getter 固化的是章节族的数据路径前缀");
            StringAssert.Contains("public static readonly int[] Chapters = new[] { 1, 2 };", getter);
            StringAssert.Contains("public static int ChapterCount => Chapters.Length;", getter);
            StringAssert.Contains("public static bool HasChapter(int chapterId)", getter,
                "新增：判断章节是否存在（不再让调用方自己遍历 Chapters）");
            StringAssert.Contains("public static IReadOnlyList<ChapterConfigChapter1> Chapter1", getter,
                "每章节访问器改为只读接口");
            StringAssert.Contains("public static IReadOnlyList<ChapterConfigChapter2> Chapter2", getter);
            StringAssert.Contains("public static ChapterConfigBase Get(int key, int chapterId = -1)", getter,
                "按章节取行的方法名是 Get（旧名 GetByID），章节号可省略（当前章节）");
            StringAssert.Contains("public static bool TryGet(int key, int chapterId, out ChapterConfigBase value)", getter);
            StringAssert.Contains("public static bool Contains(int key, int chapterId = -1) => Get(key, chapterId) != null;", getter);
            StringAssert.Contains("public static void Reload()", getter);
            StringAssert.Contains("public static void LoadFrom(int chapterId, byte[] container) => ApplyChapter(chapterId, container);", getter);
            StringAssert.Contains("public static IReadOnlyList<ChapterConfigBase> GetChapter(int chapterId = -1)", getter,
                "章节号可省略：省略（-1）= 当前章节");
            StringAssert.Contains("default: return None;", getter,
                "未知章节返回预建的空数组（旧版是 Enumerable.Empty，需要 System.Linq）");
            StringAssert.Contains("where T : ChapterConfigBase", getter);
            StringAssert.Contains("private static List<T> LoadChapter<T>(int chapterId) where T : ChapterConfigBase", getter,
                "章节加载方法的旧名是 Load<T>");

            // 章节分派：模板用 switch 语句逐章节回调（旧版是 switch 表达式 1 => Find(...)）
            StringAssert.Contains("case 1: return Find(Chapter1, key);", getter);
            StringAssert.Contains("case 2: return Find(Chapter2, key);", getter);

            // 加载路径 = 前缀 + 章节号 + 扩展名；注册也要每章节一条（否则 PreloadAll 会漏章节）
            StringAssert.Contains("ConfigTableRuntime.ReadData(DataPathPrefix + chapterId + ConfigTableRuntime.DataExtension)", getter);
            StringAssert.Contains("private sealed class Registration : IConfigTable", getter);
            StringAssert.Contains("foreach (int chapter in Chapters) ConfigTableRuntime.Register(new Registration(chapter));", getter);

            // 外层结构类：泛型版 DataFile<T>（旧名 Wrapper<T>）
            StringAssert.Contains("public sealed class DataFile<T> { public List<T> data; }", getter);

            // usings 去掉了 System.Linq（改用预建空数组 + 显式 for 循环）
            StringAssert.DoesNotContain("using System.Linq;", getter, "章节 Getter 不再需要 LINQ");
            StringAssert.DoesNotContain("Wrapper", getter, "旧的外层结构类名 Wrapper<T> 已被 DataFile<T> 取代");
            StringAssert.DoesNotContain("GetByID", getter, "旧方法名 GetByID 已被 Get(key, chapterId) 取代");
        }

        [Test]
        public void ChapterGetter_DefaultsToInjectedCurrentChapter()
        {
            CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut);
            CExcelGenerator.GenerateAllSheets(_tmpXlsx, options);
            string getter = File.ReadAllText(CExcelTestFactory.CodePath(options, "ChapterConfig", "ChapterConfigGetter.cs"));

            // 方案 B（接口注入）：生成包是独立程序集、引用不到 Assembly-CSharp 里的游戏状态，
            // 所以"当前章节"只能由游戏在启动早期喂进来，生成代码只读注入值。
            StringAssert.Contains("public static int CurrentChapterId => ConfigTableRuntime.CurrentChapterId;", getter);
            StringAssert.Contains("if (chapterId == -1) chapterId = ConfigTableRuntime.CurrentChapterId;", getter,
                "-1（省略）解析成当前章节");
            StringAssert.Contains("public static ChapterConfigBase Get(int key, int chapterId = -1)", getter);
            StringAssert.Contains("public static bool TryGet(int key, out ChapterConfigBase value) => TryGet(key, -1, out value);", getter);
            StringAssert.Contains("switch (ResolveChapter(chapterId))", getter, "所有章节查询都要先解析章节号");

            // 没配置该章节 → 警告一次 + 回退到最后一章（照抄项目既有行为：策划没配就给上一章数据）
            StringAssert.Contains("private static readonly int LastChapter = 2;", getter);
            StringAssert.Contains("if (WarnedMissingChapters.Add(chapterId))", getter, "同一个缺失章节只警告一次，别刷屏");
            StringAssert.Contains("策划没有配置 第", getter);
            StringAssert.Contains("默认给第 \" + LastChapter + \" 章数据", getter);
        }

        [Test]
        public void RuntimeTemplate_ExposesInjectedConfigContext()
        {
            CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut);
            CExcelGenerator.GenerateAllSheets(_tmpXlsx, options);
            string runtime = File.ReadAllText(Path.Combine(options.CodeFolder, "Runtime", "ConfigTableRuntime.cs"));

            StringAssert.Contains("public interface IConfigContext", runtime, "注入点是一个接口（游戏实现它）");
            StringAssert.Contains("int CurrentChapterId { get; }", runtime);
            StringAssert.Contains("public static IConfigContext Context;", runtime);
            StringAssert.Contains("public static int FallbackChapterId = 1;", runtime,
                "没注入时回退到第 1 章——对齐项目里的 ?? 1");
            StringAssert.Contains("尚未注入 IConfigContext", runtime, "缺注入要给出可操作的警告，而不是静默出错");
        }

        [Test]
        public void ChapterSubClass_InheritsBase()
        {
            CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut);
            CExcelGenerator.GenerateAllSheets(_tmpXlsx, options);
            string sub = File.ReadAllText(CExcelTestFactory.CodePath(options, "ChapterConfig", "ChapterConfigChapter1.cs"));

            StringAssert.Contains("public sealed class ChapterConfigChapter1 : ChapterConfigBase", sub);
            StringAssert.Contains("// Source sheet: ChapterConfig_1", sub, "子类仍是「一章一文件」，只是类名换成 <前缀>Chapter<N>");
            Assert.IsFalse(sub.Contains("public int Id;"), "字段应在基类，子类不重复声明");
        }

        [Test]
        public void ChapterBaseClass_DeclaresAllFields()
        {
            CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut);
            CExcelGenerator.GenerateAllSheets(_tmpXlsx, options);
            string baseClass = File.ReadAllText(CExcelTestFactory.CodePath(options, "ChapterConfig", "ChapterConfigBase.cs"));

            StringAssert.Contains("public class ChapterConfigBase", baseClass);
            StringAssert.Contains("public int Id;", baseClass);
            StringAssert.Contains("public string Name;", baseClass);
            StringAssert.Contains("public int[] Rewards;", baseClass);
        }

        [Test]
        public void GenerateAllSheets_SkipsDefaultSheetNames()
        {
            // 含 "Sheet1"（默认名约定跳过）与一个正常表
            string path = CExcelTestFactory.CreateMultiSheetTempTable(
                ("Sheet1", new[] { CExcelTestFactory.Row("X_i", 1) }),
                ("Normal", new[] { CExcelTestFactory.Row("Id_i", 1, "Name_s", "A") }));
            try
            {
                CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut);
                CExcelGenerateResult result = CExcelGenerator.GenerateAllSheets(path, options);

                Assert.IsTrue(result.Success, string.Join("\n", result.Issues));
                Assert.IsTrue(File.Exists(CExcelTestFactory.CodePath(options, "Normal", "Normal.cs")), "正常 sheet 应生成");
                Assert.IsFalse(File.Exists(CExcelTestFactory.CodePath(options, "Sheet1", "Sheet1.cs")), "默认名 Sheet1 应被跳过");
                Assert.IsFalse(File.Exists(CExcelTestFactory.DataPath(options, "Sheet1", "Sheet1")), "默认名 Sheet1 不该产出数据");
            }
            finally
            {
                CExcelTestFactory.DeleteTempFile(path);
            }
        }

        [Test]
        public void Generate_SingleSheet_ClassNameOption()
        {
            // 单 sheet 表：ClassName 选项决定类名（= 表文件夹名 = 数据名）
            string path = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Name_s", "A"),
            }, "MyTable");
            try
            {
                CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut);
                options.ClassName = "CustomName";
                CExcelGenerateResult result = CExcelGenerator.Generate(path, options);

                Assert.IsTrue(result.Success, string.Join("\n", result.Issues));
                Assert.IsTrue(File.Exists(CExcelTestFactory.CodePath(options, "CustomName", "CustomName.cs")));
                Assert.IsTrue(File.Exists(CExcelTestFactory.CodePath(options, "CustomName", "CustomNameGetter.cs")));
                Assert.IsTrue(File.Exists(CExcelTestFactory.DataPath(options, "CustomName", "CustomName")));
            }
            finally
            {
                CExcelTestFactory.DeleteTempFile(path);
            }
        }

        [Test]
        public void GeneratedCode_NoToolChinese_CommentsUseColumnNames()
        {
            // 单行表头（英文列名、无说明行）：生成代码应为纯英文（文件头英文、注释=列名）
            string path = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Name_s", "A", "Price_f", 1.5),
            }, "PlainTable");
            try
            {
                CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut);
                options.ClassName = "Plain";
                CExcelGenerateResult result = CExcelGenerator.Generate(path, options);
                Assert.IsTrue(result.Success);

                string classText = File.ReadAllText(CExcelTestFactory.CodePath(options, "Plain", "Plain.cs"));
                StringAssert.Contains("Auto-generated by CoffeeBean.Excel. Do not edit.", classText);
                StringAssert.Contains("/// <summary>Id_i</summary>", classText, "无说明行时注释用源列名");
                Assert.IsFalse(ContainsChinese(classText), "单行表头生成的代码不应含中文");
            }
            finally
            {
                CExcelTestFactory.DeleteTempFile(path);
            }
        }

        private static bool ContainsChinese(string text)
        {
            foreach (char c in text)
                if (c >= 0x4E00 && c <= 0x9FFF) return true;
            return false;
        }
    }
}
