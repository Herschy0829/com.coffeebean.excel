using System;
using System.IO;
using NUnit.Framework;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>
    /// 主键相关的两类真实场景：
    ///
    /// 1. **表里没有可做键的列**（例如只有若干 float 数值列）—— 以前生成器直接报
    ///    "未找到主键列"并不产出任何东西，于是这类表**完全拿不到数据**。
    ///    现在照样生成，只是不产出按主键查询的接口，靠 All / GetByIndex / Find / FindAll 访问。
    /// 2. **同一个键对应多行** —— 只给 Get(key) 不够（它只能给一行），必须有 GetAll(key) 拿全部。
    /// </summary>
    public class CExcelKeyTests
    {
        private string _tmpXlsx;
        private string _tmpOut;

        [SetUp]
        public void SetUp()
        {
            _tmpOut = Path.Combine(Path.GetTempPath(), "coffeebean_key_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            CExcelTestFactory.DeleteTempFile(_tmpXlsx);
            if (Directory.Exists(_tmpOut)) Directory.Delete(_tmpOut, true);
        }

        [Test]
        public void NoKeyCandidate_StillGenerates_WithWarningAndNonKeyAccessors()
        {
            // 只有 float 列：没有任何 *_i / *_l / *_s / *_e 这样的键候选
            _tmpXlsx = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Price_f", 1.5, "Ratio_f", 0.25, "Bonus_f", 3.0),
                CExcelTestFactory.Row("Price_f", 2.5, "Ratio_f", 0.50, "Bonus_f", 4.0),
            }, "NoKeyProbe");

            CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut);
            CExcelGenerateResult result = CExcelGenerator.Generate(_tmpXlsx, options);

            Assert.IsTrue(result.Success, "没有可做键的列也应当生成成功：\n" + string.Join("\n", result.Issues));
            Assert.IsTrue(result.Issues.Exists(i => i.Level == CExcelIssueLevel.Warning && i.Message.Contains("没有可做键的列")),
                "应给出「没有可做键的列」警告（提示接口面会变小），而不是静默");
            Assert.IsFalse(result.Issues.Exists(i => i.Level == CExcelIssueLevel.Error), "不应有错误级问题");

            string getter = File.ReadAllText(CExcelTestFactory.CodePath(options, "NoKeyProbe", "NoKeyProbeGetter.cs"));
            StringAssert.Contains("本表没有可做键的列", getter, "生成代码里应说明为什么没有按主键的接口");
            StringAssert.Contains("public static IReadOnlyList<NoKeyProbe> All", getter);
            StringAssert.Contains("public static NoKeyProbe GetByIndex(int index)", getter);
            StringAssert.Contains("public static NoKeyProbe Find(Func<NoKeyProbe, bool> predicate)", getter);
            StringAssert.Contains("public static List<NoKeyProbe> FindAll(Func<NoKeyProbe, bool> predicate)", getter);
            StringAssert.Contains("Reload", getter);
            StringAssert.Contains("LoadFrom", getter);

            Assert.IsFalse(getter.Contains("_byKey"), "无键表不该生成主键索引字段");
            Assert.IsFalse(getter.Contains("GetAll("), "无键表不该生成 GetAll");
            Assert.IsFalse(getter.Contains("BuildIndex"), "无键表不该生成索引构建");

            Assert.IsTrue(File.Exists(CExcelTestFactory.DataPath(options, "NoKeyProbe", "NoKeyProbe")), "数据仍应产出");
        }

        [Test]
        public void DuplicateKeys_GeneratesGetAll_AndGetKeepsFirstRow()
        {
            _tmpXlsx = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Name_s", "同键第一行"),
                CExcelTestFactory.Row("Id_i", 1, "Name_s", "同键第二行"),
                CExcelTestFactory.Row("Id_i", 2, "Name_s", "另一个键"),
            }, "DupProbe");

            CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut);
            CExcelGenerateResult result = CExcelGenerator.Generate(_tmpXlsx, options);
            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            string getter = File.ReadAllText(CExcelTestFactory.CodePath(options, "DupProbe", "DupProbeGetter.cs"));
            StringAssert.Contains("public static IReadOnlyList<DupProbe> GetAll(int key)", getter, "必须有 GetAll：一个键可能对应多行");
            StringAssert.Contains("BuildIndexAll", getter);
            StringAssert.Contains("同一个键有多行时给**第一行**", getter, "Get 的语义要写清楚是首行");
            StringAssert.Contains("if (!index.ContainsKey(item.Id)) index[item.Id] = item;", getter,
                "主键索引应保留**第一次出现**的行（不是最后一行）");
        }

        [Test]
        public void ChapterFamily_WithoutKey_GeneratesChapterAccessorsOnly()
        {
            _tmpXlsx = CExcelTestFactory.CreateMultiSheetTempTable(
                ("NoKeyChapter_1", new[] { CExcelTestFactory.Row("Price_f", 1.0, "Ratio_f", 0.1) }),
                ("NoKeyChapter_2", new[] { CExcelTestFactory.Row("Price_f", 2.0, "Ratio_f", 0.2) }));

            CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut);
            CExcelGenerateResult result = CExcelGenerator.GenerateAllSheets(_tmpXlsx, options);
            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));
            Assert.IsTrue(result.Issues.Exists(i => i.Level == CExcelIssueLevel.Warning && i.Message.Contains("没有可做键的列")),
                "章节族无键时也应有警告");

            string getter = File.ReadAllText(CExcelTestFactory.CodePath(options, "NoKeyChapter", "NoKeyChapterGetter.cs"));
            StringAssert.Contains("Chapters", getter);
            StringAssert.Contains("GetChapter(int chapterId)", getter);
            StringAssert.Contains("Chapter1", getter);
            StringAssert.Contains("HasChapter(int chapterId)", getter);
            StringAssert.Contains("本表没有可做键的列", getter);
            Assert.IsFalse(getter.Contains("_byKey"), "无键章节族不该有主键索引");
            Assert.IsFalse(getter.Contains("GetAll("), "无键章节族不该有 GetAll");
        }
    }
}
