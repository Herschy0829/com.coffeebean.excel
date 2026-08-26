using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>多 Sheet 与分章节生成测试（对齐 Idle 约定：sheet 名 前缀_数字 → 聚合 Getter 按章节查询）。</summary>
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
            CExcelGenerateResult result = CExcelGenerator.GenerateAllSheets(_tmpXlsx,
                new CExcelGenerateOptions { OutputFolder = _tmpOut, JsonResourcesFolder = _tmpOut + "/Resources", Namespace = "Config" });

            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            // 每章节：JSON（Resources 目录）+ 子类 + 章节 Getter；聚合：基类 + 聚合 Getter
            Assert.IsTrue(File.Exists(Path.Combine(_tmpOut, "Resources", "ChapterConfig_1.json")));
            Assert.IsTrue(File.Exists(Path.Combine(_tmpOut, "Resources", "ChapterConfig_2.json")));
            Assert.IsTrue(File.Exists(Path.Combine(_tmpOut, "ChapterConfigConfigBase.cs")), "应生成章节基类");
            Assert.IsTrue(File.Exists(Path.Combine(_tmpOut, "ChapterConfig_1Config.cs")), "应生成章节子类");
            Assert.IsTrue(File.Exists(Path.Combine(_tmpOut, "ChapterConfig_2Config.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(_tmpOut, "ChapterConfig_1Getter.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(_tmpOut, "ChapterConfigGetter.cs")), "应生成聚合 Getter");
        }

        [Test]
        public void ChapterGetter_ProvidesChapterQueries()
        {
            CExcelGenerator.GenerateAllSheets(_tmpXlsx, new CExcelGenerateOptions { OutputFolder = _tmpOut, JsonResourcesFolder = _tmpOut + "/Resources" });
            string getter = File.ReadAllText(Path.Combine(_tmpOut, "ChapterConfigGetter.cs"));

            StringAssert.Contains("public static readonly int[] Chapters = new[] { 1, 2 };", getter);
            StringAssert.Contains("public static List<ChapterConfig_1Config> Chapter1", getter);
            StringAssert.Contains("public static List<ChapterConfig_2Config> Chapter2", getter);
            StringAssert.Contains("GetByID(int key, int chapterId)", getter);
            StringAssert.Contains("1 => Find(Chapter1, key),", getter);
            StringAssert.Contains("2 => Find(Chapter2, key),", getter);
            StringAssert.Contains("GetChapter(int chapterId)", getter);
            StringAssert.Contains("where T : ChapterConfigConfigBase", getter);
        }

        [Test]
        public void ChapterSubClass_InheritsBase()
        {
            CExcelGenerator.GenerateAllSheets(_tmpXlsx, new CExcelGenerateOptions { OutputFolder = _tmpOut, JsonResourcesFolder = _tmpOut + "/Resources" });
            string sub = File.ReadAllText(Path.Combine(_tmpOut, "ChapterConfig_1Config.cs"));

            StringAssert.Contains("public sealed class ChapterConfig_1Config : ChapterConfigConfigBase", sub);
            Assert.IsFalse(sub.Contains("public int Id;"), "字段应在基类，子类不重复声明");
        }

        [Test]
        public void ChapterBaseClass_DeclaresAllFields()
        {
            CExcelGenerator.GenerateAllSheets(_tmpXlsx, new CExcelGenerateOptions { OutputFolder = _tmpOut, JsonResourcesFolder = _tmpOut + "/Resources" });
            string baseClass = File.ReadAllText(Path.Combine(_tmpOut, "ChapterConfigConfigBase.cs"));

            StringAssert.Contains("public class ChapterConfigConfigBase", baseClass);
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
                CExcelGenerateResult result = CExcelGenerator.GenerateAllSheets(path,
                    new CExcelGenerateOptions { OutputFolder = _tmpOut, JsonResourcesFolder = _tmpOut + "/Resources" });

                Assert.IsTrue(result.Success, string.Join("\n", result.Issues));
                Assert.IsTrue(File.Exists(Path.Combine(_tmpOut, "Normal.cs")), "正常 sheet 应生成");
                Assert.IsFalse(File.Exists(Path.Combine(_tmpOut, "Sheet1.cs")), "默认名 Sheet1 应被跳过");
            }
            finally
            {
                CExcelTestFactory.DeleteTempFile(path);
            }
        }

        [Test]
        public void Generate_SingleSheet_ClassNameOption()
        {
            // 单 sheet 表：ClassName 选项决定类名
            string path = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Name_s", "A"),
            }, "MyTable");
            try
            {
                CExcelGenerateResult result = CExcelGenerator.Generate(path,
                    new CExcelGenerateOptions { OutputFolder = _tmpOut, JsonResourcesFolder = _tmpOut + "/Resources", ClassName = "CustomName" });

                Assert.IsTrue(result.Success, string.Join("\n", result.Issues));
                Assert.IsTrue(File.Exists(Path.Combine(_tmpOut, "CustomName.cs")));
                Assert.IsTrue(File.Exists(Path.Combine(_tmpOut, "CustomNameGetter.cs")));
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
                CExcelGenerateResult result = CExcelGenerator.Generate(path,
                    new CExcelGenerateOptions { OutputFolder = _tmpOut, JsonResourcesFolder = _tmpOut + "/Resources", ClassName = "Plain" });
                Assert.IsTrue(result.Success);

                string classText = File.ReadAllText(Path.Combine(_tmpOut, "Plain.cs"));
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
