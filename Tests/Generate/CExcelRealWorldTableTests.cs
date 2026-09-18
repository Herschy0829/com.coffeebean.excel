using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>
    /// **真实配置表长相**的测试：说明行 / 图例行 / 边上贴的草稿块，以及 `_` 分隔的数组。
    ///
    /// 这些不是拍脑袋想的用例，是拿一个真实项目（28 张表 / 57 个 sheet）dogfood 出来的：
    /// 表头下面往往还有一段"字段 | 说明"的图例、或者某列旁边贴一串临时算的数，
    /// 它们的主键是空的；再加上数组普遍写成 `13_100`。旧行为下这两件小事会让**整批表**生成失败。
    /// </summary>
    public class CExcelRealWorldTableTests
    {
        private string _tmpXlsx;
        private string _tmpOut;

        [TearDown]
        public void TearDown()
        {
            if (_tmpXlsx != null) CExcelTestFactory.DeleteTempFile(_tmpXlsx);
            if (_tmpOut != null && Directory.Exists(_tmpOut)) Directory.Delete(_tmpOut, true);
            _tmpXlsx = null;
            _tmpOut = null;
        }

        private CExcelGenerateResult Generate(IDictionary<string, object>[] rows, CExcelGenerateOptions options = null)
        {
            _tmpXlsx = CExcelTestFactory.CreateTempTable(rows, "Building");
            _tmpOut = Path.Combine(Path.GetTempPath(), "coffeebean_rw_" + Guid.NewGuid().ToString("N"));
            options = options ?? new CExcelGenerateOptions();
            options.OutputFolder = _tmpOut;
            options.ClassName = "Building";
            options.JsonResourcesFolder = _tmpOut + "/Resources";
            options.EncryptJson = false;
            return CExcelGenerator.Generate(_tmpXlsx, options);
        }

        // ========== 说明行 / 图例行 ==========

        [Test]
        public void RowsWithoutKey_AreSkippedWithAWarning()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("ID_i", 1001, "Name_s", "宿舍", "Cost_l", 1000),
                CExcelTestFactory.Row("ID_i", 1002, "Name_s", "伐木场", "Cost_l", 1500),
                // 下面是"图例/草稿"行：主键空着，旁边的列塞了说明文字
                CExcelTestFactory.Row("ID_i", null, "Name_s", null, "Cost_l", "说明"),
                CExcelTestFactory.Row("ID_i", null, "Name_s", null, "Cost_l", "每级消耗"),
                CExcelTestFactory.Row("ID_i", null, "Name_s", "建筑名", "Cost_l", "宿舍"),
            });

            Assert.IsTrue(result.Success, "说明行不该让整张表生成失败：" + string.Join("\n", result.Issues));

            // 警告要列出行号（不是悄悄吞掉）
            CExcelIssue warning = result.Issues.Find(i => i.Level == CExcelIssueLevel.Warning && i.Message.Contains("跳过"));
            Assert.IsNotNull(warning, "跳过说明行必须出警告：" + string.Join("\n", result.Issues));
            StringAssert.Contains("4, 5, 6", warning.Message);

            // 产物里只有 2 行数据（字段名 "ID_i" → "ID"）
            string json = File.ReadAllText(Path.Combine(_tmpOut, "Resources", "Building.json"));
            Assert.AreEqual(2, CountOccurrences(json, "\"ID\":"), "JSON 里只该有真实数据行：" + json);
        }

        [Test]
        public void SkippingCanBeTurnedOff()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("ID_i", 1, "Cost_l", 100),
                CExcelTestFactory.Row("ID_i", null, "Cost_l", "说明"),
            }, new CExcelGenerateOptions { SkipRowsWithoutKey = false });

            Assert.IsFalse(result.Success, "关掉跳过后，主键空行会被当成数据行 → 主键为空是错误");
            Assert.IsTrue(result.Issues.Exists(i => i.Level == CExcelIssueLevel.Error && i.Message.Contains("主键列是空的")),
                string.Join("\n", result.Issues));
        }

        /// <summary>
        /// 整张表**没有任何一列**能做合法主键 → 报错（多半是表头检测选错行），**不能生成空表**。
        /// 注意：只有 ID 列坏、另一列（如 Name_s）每行都合法时，主键会自动落到那一列上 —— 那是正确行为。
        /// </summary>
        [Test]
        public void AllRowsWithoutKey_IsAnError_NotEmptyOutput()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("ID_i", "字段", "Note_f", 1.5),
                CExcelTestFactory.Row("ID_i", "ID", "Note_f", 2.5),
            });

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Issues.Exists(i => i.Level == CExcelIssueLevel.Error && i.Message.Contains("所有行")),
                string.Join("\n", result.Issues));
            Assert.IsFalse(File.Exists(Path.Combine(_tmpOut, "Resources", "Building.json")), "失败时不该产出空 JSON");
        }

        /// <summary>ID 列坏、但另一列（Name_s）每行都合法时，主键自动落到那一列 —— 表照样能生成。</summary>
        [Test]
        public void PrimaryKeyFallsBackToTheColumnThatIsValidEverywhere()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("ID_i", "字段", "Name_s", "A", "Cost_l", 100),
                CExcelTestFactory.Row("ID_i", "ID", "Name_s", "B", "Cost_l", 200),
            });

            // 主键落到 Name_s；ID_i 还是错的（文本填进 int 列）→ 仍然报错，但报的是"ID_i 不是整数"
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Issues.Exists(i => i.Column == "ID_i" && i.Message.Contains("不是整数")),
                string.Join("\n", result.Issues));
            Assert.IsFalse(result.Issues.Exists(i => i.Message.Contains("跳过")),
                "主键落在 Name_s 上（每行都合法）→ 一行都不该被跳过，也就不该有跳过警告：" + string.Join("\n", result.Issues));
        }

        /// <summary>主键列选择偏好"每行都有值"的列：ID 列空一片时不该选中它。</summary>
        [Test]
        public void PrimaryKeyPrefersColumnThatIsFilledEverywhere()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                // Note_l 也是可做键的类型，但一行空一行有；Name_s 每行都有值
                CExcelTestFactory.Row("Note_l", 10, "Name_s", "A", "Cost_l", 100),
                CExcelTestFactory.Row("Note_l", null, "Name_s", "B", "Cost_l", 200),
            });

            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));
            string getter = File.ReadAllText(Path.Combine(_tmpOut, "BuildingGetter.cs"));
            StringAssert.Contains("Get(string key)", getter, "主键应落在每行都有值的 Name_s 上");
        }

        // ========== 真实数组写法 ==========

        [Test]
        public void UnderscoreSeparatedArrays_FlowThroughGeneration()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("ID_i", 1, "Award_ia", "13_100", "Ratio_fa", "0.2_0.8_1", "Ids_la", "200_500"),
            });

            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            string classText = File.ReadAllText(Path.Combine(_tmpOut, "Building.cs"));
            StringAssert.Contains("public int[] Award;", classText);
            StringAssert.Contains("public float[] Ratio;", classText);
            StringAssert.Contains("public long[] Ids;", classText);

            string json = File.ReadAllText(Path.Combine(_tmpOut, "Resources", "Building.json"));
            StringAssert.Contains("\"Award\":[13,100]", json);
            StringAssert.Contains("\"Ratio\":[0.2,0.8,1]", json);
            StringAssert.Contains("\"Ids\":[200,500]", json);
        }

        /// <summary>真实数据里的类型不匹配必须报出来（这些值以前会被安静地写成 0）。</summary>
        [Test]
        public void RealDataMismatches_AreReportedWithRowNumbers()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("ID_i", 1, "Level_i", 3.24, "Big_i", "20545344000"),
            });

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Issues.Exists(i => i.Level == CExcelIssueLevel.Error && i.Column == "Level_i" && i.Message.Contains("3.24")),
                "小数填进 int 列要报：" + string.Join("\n", result.Issues));
            Assert.IsTrue(result.Issues.Exists(i => i.Message.Contains("超出")),
                "超出 int32 的值要报（以前会溢出）：" + string.Join("\n", result.Issues));
        }

        private static int CountOccurrences(string text, string sub)
        {
            int count = 0, idx = 0;
            while ((idx = text.IndexOf(sub, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += sub.Length;
            }
            return count;
        }
    }
}
