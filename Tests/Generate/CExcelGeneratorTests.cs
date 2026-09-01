using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>CExcelGenerator 生成测试：产物内容断言（JSON 合法 / C# 类 / Getter）。</summary>
    public class CExcelGeneratorTests
    {
        private string _tmpXlsx;
        private string _tmpOut;

        [SetUp]
        public void SetUp()
        {
            _tmpXlsx = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Name_s", "新手礼包", "Price_f", 6.5, "Rewards_ia", "100;200;300", "Enabled_b", 1),
                CExcelTestFactory.Row("Id_i", 2, "Name_s", "月卡", "Price_f", 30.0, "Rewards_ia", "500", "Enabled_b", 0),
            }, "coffeebean_gen");
            _tmpOut = Path.Combine(Path.GetTempPath(), "coffeebean_gen_out_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            CExcelTestFactory.DeleteTempFile(_tmpXlsx);
            if (Directory.Exists(_tmpOut)) Directory.Delete(_tmpOut, true);
        }

        private CExcelGenerateResult Generate(string className = null, string ns = "Config")
        {
            var options = new CExcelGenerateOptions
            {
                OutputFolder = _tmpOut,
                Namespace = ns,
                ClassName = className,
                JsonResourcesFolder = _tmpOut + "/Resources", // 测试自包含：JSON 输出到临时 Resources 目录
                ResourcesPath = "Configs",
                EncryptJson = false, // 现有断言读明文 JSON；加密行为单独测试
            };
            CExcelGenerateResult result = CExcelGenerator.Generate(_tmpXlsx, options);
            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));
            return result;
        }

        [Test]
        public void Generate_Json_GoesToResourcesFolder()
        {
            Generate("TestTable");

            // JSON 应生成在 Resources 目录（而非代码输出目录），运行时 Resources.Load 才能读到
            Assert.IsTrue(File.Exists(Path.Combine(_tmpOut, "Resources", "TestTable.json")), "JSON 应输出到 Resources 目录");
            Assert.IsFalse(File.Exists(Path.Combine(_tmpOut, "TestTable.json")), "代码输出目录不应放 JSON");
        }

        [Test]
        public void Generate_CreatesIsolatedAsmdef()
        {
            Generate("TestTable");

            // 生成代码目录应有独立 asmdef（程序集级增量编译隔离）
            string asmdefPath = Path.Combine(_tmpOut, "Config.Generated.asmdef");
            Assert.IsTrue(File.Exists(asmdefPath), "应生成独立 asmdef 隔离生成代码");
            string json = File.ReadAllText(asmdefPath);
            Assert.IsTrue(json.Contains("\"Config.Generated\""), "asmdef 名称应为 Config.Generated");
            Assert.IsTrue(json.Contains("\"rootNamespace\": \"Config\""), "rootNamespace 应为生成代码命名空间");
            Assert.IsTrue(json.Contains("\"autoReferenced\": true"), "应 autoReferenced 供业务代码直接引用");
        }

        [Test]
        public void Generate_DefaultNamespace_IsCoffeeBean()
        {
            // 默认命名空间应为 CoffeeBean 根命名空间（using CoffeeBean; 即可访问生成类）
            var options = new CExcelGenerateOptions
            {
                OutputFolder = _tmpOut,
                ClassName = "TestTable",
                JsonResourcesFolder = _tmpOut + "/Resources",
                ResourcesPath = "Configs",
                EncryptJson = false,
                Namespace = null, // 不传 → 用默认值
            };
            CExcelGenerateResult result = CExcelGenerator.Generate(_tmpXlsx, options);
            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            string classContent = File.ReadAllText(Path.Combine(_tmpOut, "TestTable.cs"));
            Assert.IsTrue(classContent.Contains("namespace CoffeeBean"), "默认命名空间应为 CoffeeBean");

            // asmdef 也应跟随默认命名空间
            string asmdefPath = Path.Combine(_tmpOut, "CoffeeBean.Generated.asmdef");
            Assert.IsTrue(File.Exists(asmdefPath), "asmdef 名称应跟随默认命名空间 CoffeeBean");
        }

        [Test]
        public void Generate_CustomNamespace_AsmdefNameFollows()
        {
            Generate("TestTable", ns: "MyGame.Config");

            string asmdefPath = Path.Combine(_tmpOut, "MyGame.Config.Generated.asmdef");
            Assert.IsTrue(File.Exists(asmdefPath), "asmdef 名称应跟随命名空间");
            string json = File.ReadAllText(asmdefPath);
            Assert.IsTrue(json.Contains("\"MyGame.Config.Generated\""), "asmdef 名称应为 MyGame.Config.Generated");
        }

        [Test]
        public void EnsureAsmdef_IsIdempotent()
        {
            // 手动放一个自定义 asmdef，验证生成不覆盖
            string customPath = Path.Combine(_tmpOut, "Config.Generated.asmdef");
            Directory.CreateDirectory(_tmpOut);
            File.WriteAllText(customPath, "custom", new System.Text.UTF8Encoding(false));

            CExcelGenerator.EnsureGeneratedAsmdef(_tmpOut, "Config");

            Assert.AreEqual("custom", File.ReadAllText(customPath), "已存在的 asmdef 不应被覆盖");
        }

        [Test]
        public void Generate_Getter_AssetPath_MatchesResourcesPath()
        {
            Generate("TestTable");
            string getterText = File.ReadAllText(Path.Combine(_tmpOut, "TestTableGetter.cs"));

            StringAssert.Contains("AssetPath = \"Configs/TestTable\"", getterText, "Getter 应通过 Resources 相对路径加载");
        }

        [Test]
        public void Generate_ProducesThreeFiles()
        {
            CExcelGenerateResult result = Generate("TestTable");

            Assert.AreEqual(3, result.GeneratedFiles.Count);
            Assert.IsTrue(File.Exists(result.GeneratedFiles[0]));
            Assert.IsTrue(File.Exists(result.GeneratedFiles[1]));
            Assert.IsTrue(File.Exists(result.GeneratedFiles[2]));
        }

        [Test]
        public void Generate_Json_ValidAndDataCount()
        {
            Generate("TestTable");
            string json = File.ReadAllText(Path.Combine(_tmpOut, "Resources", "TestTable.json"));

            StringAssert.StartsWith("{\"data\":[", json);
            Assert.AreEqual(2, CountOccurrences(json, "\"Id\":"), "应有 2 行数据");

            // 用 JsonUtility 验证可解析（包装对象）
            Wrapper probe = JsonUtility.FromJson<Wrapper>(json);
            Assert.IsNotNull(probe);
            Assert.AreEqual(2, probe.data.Count);
            Assert.AreEqual(1, probe.data[0].Id);
            Assert.AreEqual("新手礼包", probe.data[0].Name);
            Assert.AreEqual(3, probe.data[0].Rewards.Length);
            Assert.AreEqual(100, probe.data[0].Rewards[0]);
            Assert.IsTrue(probe.data[0].Enabled);
        }

        [Test]
        public void Generate_Class_FieldTypesAndNames()
        {
            Generate("TestTable");
            string classText = File.ReadAllText(Path.Combine(_tmpOut, "TestTable.cs"));

            StringAssert.Contains("public sealed class TestTable", classText);
            StringAssert.Contains("public int Id;", classText);
            StringAssert.Contains("public string Name;", classText);
            StringAssert.Contains("public float Price;", classText);
            StringAssert.Contains("public int[] Rewards;", classText);
            StringAssert.Contains("public bool Enabled;", classText);
            StringAssert.Contains("namespace Config", classText);
        }

        [Test]
        public void Generate_Getter_PrimaryKeyAndLoad()
        {
            Generate("TestTable");
            string getterText = File.ReadAllText(Path.Combine(_tmpOut, "TestTableGetter.cs"));

            StringAssert.Contains("public static TestTable Get(int key)", getterText);
            StringAssert.Contains("Resources.Load<TextAsset>", getterText);
            StringAssert.Contains("JsonUtility.FromJson<Wrapper>", getterText);
            StringAssert.Contains("item.Id", getterText, "主键索引应按 Id 建立");
        }

        [Test]
        public void Generate_CustomNamespaceAndPrimaryKey()
        {
            var options = new CExcelGenerateOptions
            {
                OutputFolder = _tmpOut,
                Namespace = "MyGame.Data",
                ClassName = "CustomTable",
                PrimaryKey = "Name_s",
                JsonResourcesFolder = _tmpOut + "/Resources",
            };
            CExcelGenerateResult result = CExcelGenerator.Generate(_tmpXlsx, options);
            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            string getterText = File.ReadAllText(Path.Combine(_tmpOut, "CustomTableGetter.cs"));
            StringAssert.Contains("namespace MyGame.Data", getterText);
            StringAssert.Contains("public static CustomTable Get(string key)", getterText, "自定义主键应为 string");
        }

        [Test]
        public void Generate_NoPrimaryKeyColumn_ReturnsError()
        {
            // 只有非主键列（float/string 数组等）
            string bad = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Price_f", 1.5, "Tags_sa", "a;b"),
            }, "coffeebean_bad");
            try
            {
                var result = CExcelGenerator.Generate(bad, new CExcelGenerateOptions { OutputFolder = _tmpOut });
                Assert.IsFalse(result.Success);
                StringAssert.Contains("主键", result.Issues[0].Message);
            }
            finally
            {
                CExcelTestFactory.DeleteTempFile(bad);
            }
        }

        [Test]
        public void Generate_FileMissing_ReturnsError()
        {
            var result = CExcelGenerator.Generate(Path.Combine(Path.GetTempPath(), "missing.xlsx"),
                new CExcelGenerateOptions { OutputFolder = _tmpOut });
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void GenerateFolder_BatchSkipsTempFiles()
        {
            // 批量：目录里放正式表 + ~$ 临时表
            string folder = Path.Combine(Path.GetTempPath(), "coffeebean_batch_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string a = CExcelTestFactory.CreateTempTable(new[]
                {
                    CExcelTestFactory.Row("Id_i", 1, "Name_s", "A"),
                }, "A");
                File.Move(a, Path.Combine(folder, "A.xlsx"));

                string b = CExcelTestFactory.CreateTempTable(new[]
                {
                    CExcelTestFactory.Row("X_i", 1),
                }, "~$A");
                File.Move(b, Path.Combine(folder, "~$A.xlsx"));

                CExcelGenerateResult result = CExcelGenerator.GenerateFolder(folder,
                    new CExcelGenerateOptions { OutputFolder = _tmpOut, JsonResourcesFolder = _tmpOut + "/Resources" });

                Assert.IsTrue(result.Success, string.Join("\n", result.Issues));
                Assert.AreEqual(3, result.GeneratedFiles.Count, "只生成正式表（跳过 ~$ 临时表）");
                Assert.IsTrue(File.Exists(Path.Combine(_tmpOut, "A.cs")));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static int CountOccurrences(string text, string sub)
        {
            int count = 0;
            int idx = 0;
            while ((idx = text.IndexOf(sub, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += sub.Length;
            }
            return count;
        }

        [Serializable]
        private sealed class Wrapper
        {
            public List<Row> data;
        }

        [Serializable]
        private sealed class Row
        {
            public int Id;
            public string Name;
            public float Price;
            public int[] Rewards;
            public bool Enabled;
        }
    }
}
