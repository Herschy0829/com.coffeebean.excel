using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>
    /// CExcelGenerator 生成测试：产物内容断言（数据容器 / C# 类 / Getter）。
    ///
    /// 0.6.0 起产物是"内嵌包"布局：一切都在 CodeFolder 之下，**不再写进 Assets**——
    /// 代码 <c>&lt;包&gt;/&lt;表&gt;/Code/</c>、数据 <c>&lt;包&gt;/&lt;表&gt;/Data/*.cbcfg</c>，
    /// 包根另有骨架（package.json / 标记文件 / asmdef / Runtime 支撑代码）。
    /// </summary>
    public class CExcelGeneratorTests
    {
        private string _tmpXlsx;
        private string _tmpOut;
        private CExcelGenerateOptions _options;

        [SetUp]
        public void SetUp()
        {
            _tmpXlsx = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Name_s", "新手礼包", "Price_f", 6.5, "Rewards_ia", "100;200;300", "Enabled_bool", "true", "Gold_b", "12345678901234567890"),
                CExcelTestFactory.Row("Id_i", 2, "Name_s", "月卡", "Price_f", 30.0, "Rewards_ia", "500", "Enabled_bool", "false", "Gold_b", "999"),
            }, "coffeebean_gen");
            _tmpOut = Path.Combine(Path.GetTempPath(), "coffeebean_gen_out_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            CExcelTestFactory.DeleteTempFile(_tmpXlsx);
            if (Directory.Exists(_tmpOut)) Directory.Delete(_tmpOut, true);
            _options = null;
        }

        /// <summary>生成到本用例的临时包（数据默认压缩 + 加密，走真实链路）。</summary>
        private CExcelGenerateResult Generate(string className = null, string ns = "Config")
        {
            _options = CExcelTestFactory.TempPackageOptions(_tmpOut, ns);
            _options.ClassName = className;
            CExcelGenerateResult result = CExcelGenerator.Generate(_tmpXlsx, _options);
            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));
            return result;
        }

        private string CodePathOf(string tableFolder, string fileName)
            => CExcelTestFactory.CodePath(_options, tableFolder, fileName);

        private string DataPathOf(string tableFolder, string dataName)
            => CExcelTestFactory.DataPath(_options, tableFolder, dataName);

        /// <summary>
        /// 回归断言：包内除包根骨架（package.json / coffeebean.configgen.json）之外不允许出现任何 .json。
        /// 密文 .json 会被 spine-unity 之类第三方导入器逐个 IsXxxData 检查并刷屏报错 ——
        /// 这正是数据改成 .cbcfg 容器、且不再写进 Assets 的原因，所以必须钉死。
        /// </summary>
        private void AssertNoStrayJsonInPackage()
        {
            var stray = new List<string>();
            foreach (string path in Directory.GetFiles(_options.CodeFolder, "*.json", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(path);
                if (name != "package.json" && name != "coffeebean.configgen.json") stray.Add(path);
            }

            CollectionAssert.IsEmpty(stray,
                "包内不应存在骨架之外的 .json（数据一律走 .cbcfg 容器）：" + string.Join(", ", stray));
        }

        [Test]
        public void Generate_Data_GoesToTableDataFolder()
        {
            Generate("TestTable");

            // 数据是二进制容器，落在 <包>/<表>/Data/ 下（与代码同包，运行时按包名解析到 StreamingAssets）
            string dataPath = DataPathOf("TestTable", "TestTable");
            Assert.IsTrue(File.Exists(dataPath), "数据应输出到 <包>/<表>/Data/：" + dataPath);

            // 解容器后能拿到真实数据（旧断言"JSON 输出到 Resources 目录"的新写法）
            string json = CExcelTestFactory.ReadDataJson(dataPath);
            StringAssert.Contains("\"Name\"", json);
            StringAssert.Contains("新手礼包", json);

            AssertNoStrayJsonInPackage();
        }

        [Test]
        public void Generate_CreatesIsolatedAsmdef()
        {
            Generate("TestTable");

            // asmdef 在**包根**（代码在 <包>/<表>/Code/ 下），程序集级增量编译隔离
            string packageRoot = _options.CodeFolder;
            string asmdefPath = Path.Combine(packageRoot, "Config.Generated.asmdef");
            Assert.IsTrue(File.Exists(asmdefPath), "应生成独立 asmdef 隔离生成代码: " + asmdefPath);
            string json = File.ReadAllText(asmdefPath);
            Assert.IsTrue(json.Contains("\"Config.Generated\""), "asmdef 名称应为 Config.Generated");
            Assert.IsTrue(json.Contains("\"rootNamespace\": \"Config\""), "rootNamespace 应为生成代码命名空间");
            Assert.IsTrue(json.Contains("\"autoReferenced\": true"), "应 autoReferenced 供业务代码直接引用");

            // asmdef 是包骨架的一部分：包根还应齐 package.json / 构建标记 / 运行时支撑代码
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "package.json")),
                "包根应有 package.json（Unity 靠它把目录当成包）");
            StringAssert.Contains("\"com.coffeebean.test.generated\"",
                File.ReadAllText(Path.Combine(packageRoot, "package.json")), "package.json 的 name 应取 PackageName");
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "coffeebean.configgen.json")),
                "包根应有构建标记文件（构建钩子靠它把 <表>/Data 挂进 StreamingAssets）");
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "Runtime", "ConfigTableRuntime.cs")),
                "包根 Runtime/ 下应有运行时支撑代码（容器解码实现）");
        }

        [Test]
        public void Generate_DefaultNamespace_IsCoffeeBean()
        {
            // 默认命名空间应为 CoffeeBean 根命名空间（using CoffeeBean; 即可访问生成类）
            CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut, null); // Namespace 不传 → 用默认值
            options.ClassName = "TestTable";
            CExcelGenerateResult result = CExcelGenerator.Generate(_tmpXlsx, options);
            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            string classContent = File.ReadAllText(CExcelTestFactory.CodePath(options, "TestTable", "TestTable.cs"));
            Assert.IsTrue(classContent.Contains("namespace CoffeeBean"), "默认命名空间应为 CoffeeBean");

            // asmdef 也应跟随默认命名空间（落在包根）
            string asmdefPath = Path.Combine(options.CodeFolder, "CoffeeBean.Generated.asmdef");
            Assert.IsTrue(File.Exists(asmdefPath), "asmdef 名称应跟随默认命名空间 CoffeeBean");
        }

        [Test]
        public void Generate_CustomNamespace_AsmdefNameFollows()
        {
            Generate("TestTable", ns: "MyGame.Config");

            string asmdefPath = Path.Combine(_options.CodeFolder, "MyGame.Config.Generated.asmdef");
            Assert.IsTrue(File.Exists(asmdefPath), "asmdef 名称应跟随命名空间");
            string json = File.ReadAllText(asmdefPath);
            Assert.IsTrue(json.Contains("\"MyGame.Config.Generated\""), "asmdef 名称应为 MyGame.Config.Generated");
        }

        [Test]
        public void EnsureAsmdef_IsIdempotent()
        {
            // 手动在包根放一个自定义 asmdef，验证生成不覆盖
            string packageRoot = CExcelTestFactory.TempPackageOptions(_tmpOut).CodeFolder;
            Directory.CreateDirectory(packageRoot);
            string customPath = Path.Combine(packageRoot, "Config.Generated.asmdef");
            File.WriteAllText(customPath, "custom", new System.Text.UTF8Encoding(false));

            CExcelGenerator.EnsureGeneratedAsmdef(packageRoot, "Config");

            Assert.AreEqual("custom", File.ReadAllText(customPath), "已存在的 asmdef 不应被覆盖");
        }

        [Test]
        public void Generate_Getter_DataPath_IsTableScoped()
        {
            Generate("TestTable");
            string getterText = File.ReadAllText(CodePathOf("TestTable", "TestTableGetter.cs"));

            // Getter 里固化的是"包内相对路径"：<表文件夹>/Data/<数据名>.cbcfg
            StringAssert.Contains("private const string DataPath = \"TestTable/Data/TestTable.cbcfg\";", getterText,
                "DataPath 常量应是表作用域的包内相对路径");
            Assert.IsTrue(File.Exists(DataPathOf("TestTable", "TestTable")),
                "DataPath 常量指向的相对路径必须真的落在生成出来的数据文件上");

            // 0.6.0 起没有 AssetPath / Resources.Load / 内嵌 Decode(byte[])：统一走运行时支撑代码
            StringAssert.Contains("ConfigTableRuntime.ReadData(DataPath)", getterText, "加载走 ConfigTableRuntime.ReadData");
            StringAssert.Contains("internal static List<TestTable> ApplyContainer(byte[] container)", getterText);
            StringAssert.Contains("ConfigTableRuntime.Decode(container, out error)", getterText);
            StringAssert.Contains("using Newtonsoft.Json;", getterText,
                "解容器后要用 Newtonsoft 反序列化（JsonUtility 读不回本工具支持的全部类型）");
            StringAssert.Contains("JsonConvert.DeserializeObject<DataFile>(json)", getterText,
                "外层结构类的新名字是 DataFile（旧名 Wrapper）");
            StringAssert.Contains("private sealed class Registration : IConfigTable", getterText);
            StringAssert.Contains("ConfigTableRuntime.Register(new Registration())", getterText);
            StringAssert.Contains("[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]", getterText);

            StringAssert.DoesNotContain("Resources.Load", getterText, "不应再依赖 Resources 加载");
            StringAssert.DoesNotContain("AssetPath", getterText, "旧的 AssetPath 已删除");
            StringAssert.DoesNotContain("Wrapper", getterText, "旧的外层结构类名 Wrapper 已被 DataFile 取代");
        }

        [Test]
        public void Generate_ProducesThreeFiles()
        {
            CExcelGenerateResult result = Generate("TestTable");

            // 每张表三件套：数据容器 + 数据类 + Getter（包骨架文件不计入 GeneratedFiles）
            Assert.AreEqual(3, result.GeneratedFiles.Count);
            Assert.IsTrue(File.Exists(result.GeneratedFiles[0]));
            Assert.IsTrue(File.Exists(result.GeneratedFiles[1]));
            Assert.IsTrue(File.Exists(result.GeneratedFiles[2]));

            Assert.IsTrue(File.Exists(DataPathOf("TestTable", "TestTable")), "三件套应含数据容器");
            Assert.IsTrue(File.Exists(CodePathOf("TestTable", "TestTable.cs")), "三件套应含数据类");
            Assert.IsTrue(File.Exists(CodePathOf("TestTable", "TestTableGetter.cs")), "三件套应含 Getter");
        }

        [Test]
        public void Generate_Json_ValidAndDataCount()
        {
            Generate("TestTable");
            string json = CExcelTestFactory.ReadDataJson(CExcelTestFactory.DataPath(_options, "TestTable", "TestTable"));

            StringAssert.StartsWith("{\"data\":[", json);
            Assert.AreEqual(2, CountOccurrences(json, "\"Id\":"), "应有 2 行数据");

            // 用 Newtonsoft 验证可解析（包装对象）—— 生成的 Getter 用的就是这条路径
            Wrapper probe = (Wrapper)CExcelJsonBackend.Deserialize(json, typeof(Wrapper));
            Assert.IsNotNull(probe);
            Assert.IsNotNull(probe.data, "data 字段必须读出来（null 说明字段名/形状对不上）");
            Assert.AreEqual(2, probe.data.Count);
            Assert.AreEqual(1, probe.data[0].Id);
            Assert.AreEqual("新手礼包", probe.data[0].Name);
            Assert.AreEqual(3, probe.data[0].Rewards.Length);
            Assert.AreEqual(100, probe.data[0].Rewards[0]);
            Assert.IsTrue(probe.data[0].Enabled);
            Assert.AreEqual("12345678901234567890", probe.data[0].Gold.ToString(),
                "大整数必须一位不差地读回来（这正是 JsonUtility 做不到、必须换 Newtonsoft 的原因）");
        }

        [Test]
        public void Generate_Class_FieldTypesAndNames()
        {
            Generate("TestTable");
            string classText = File.ReadAllText(CodePathOf("TestTable", "TestTable.cs"));

            StringAssert.Contains("public sealed class TestTable", classText);
            StringAssert.Contains("public int Id;", classText);
            StringAssert.Contains("public string Name;", classText);
            StringAssert.Contains("public float Price;", classText);
            StringAssert.Contains("public int[] Rewards;", classText);
            StringAssert.Contains("public bool Enabled;", classText);
            StringAssert.Contains("public System.Numerics.BigInteger Gold;", classText);
            StringAssert.Contains("namespace Config", classText);
            StringAssert.Contains("// Generator template: v" + CExcelGenerator.TemplateVersion, classText,
                "产物里要能看出是哪版模板生成的（增量生成器按它判断要不要重新生成）");
        }

        [Test]
        public void Generate_Getter_PrimaryKeyAndLoad()
        {
            Generate("TestTable");
            string getterText = File.ReadAllText(CodePathOf("TestTable", "TestTableGetter.cs"));

            StringAssert.Contains("public static TestTable Get(int key)", getterText);
            StringAssert.Contains("ConfigTableRuntime.ReadData(DataPath)", getterText,
                "数据由生成的运行时支撑代码按包解析（Player 下走 StreamingAssets）");

            // 加载器暴露的查询面（模板 v3）：懒加载状态 + 计数 + 只读全量 + 主键/下标/谓词查询
            StringAssert.Contains("public static bool IsLoaded => _all != null;", getterText);
            StringAssert.Contains("public static int Count => All.Count;", getterText,
                "Count 必须走 All（走 _all 会绕开懒加载，一进游戏就是 0 行）");
            StringAssert.Contains("public static IReadOnlyList<TestTable> All => _all ??= Load();", getterText,
                "All 改为只读接口（防止外部改缓存）且懒加载");
            StringAssert.Contains("public static bool TryGet(int key, out TestTable value)", getterText);
            StringAssert.Contains("public static bool Contains(int key) => Get(key) != null;", getterText);
            StringAssert.Contains("public static TestTable GetByIndex(int index)", getterText);
            StringAssert.Contains("public static TestTable Find(Func<TestTable, bool> predicate)", getterText);
            StringAssert.Contains("public static List<TestTable> FindAll(Func<TestTable, bool> predicate)", getterText);
            StringAssert.Contains("public static void Reload()", getterText);
            StringAssert.Contains("public static void LoadFrom(byte[] container) => ApplyContainer(container);", getterText);

            StringAssert.Contains("item.Id", getterText, "主键索引应按 Id 建立");
            StringAssert.Contains("_byKey ??= BuildIndex();", getterText, "主键索引懒建，且只建一次");

            // 生成文件顶部的 usings 现在固定这四条（Func<> / IReadOnlyList<> 都靠它们）
            int usingsAt = getterText.IndexOf("using System;", StringComparison.Ordinal);
            Assert.GreaterOrEqual(usingsAt, 0, "生成的 Getter 需要 using System;");
            StringAssert.Contains("using System.Collections.Generic;", getterText);
            StringAssert.Contains("using Newtonsoft.Json;", getterText, "生成的 Getter 用 Newtonsoft 反序列化");
            StringAssert.Contains("using UnityEngine;", getterText, "运行时支撑代码与 Debug 都在 UnityEngine 下");

            // data 为 null 时要兜住（Newtonsoft 不会给空 List）
            StringAssert.Contains("_all = file != null && file.data != null", getterText);

            // 外层结构类：必须 public sealed 且名字是 DataFile —— Newtonsoft 要能构造它，私有嵌套类型不可靠
            StringAssert.Contains("public sealed class DataFile { public List<TestTable> data; }", getterText);
            StringAssert.DoesNotContain("Wrapper", getterText, "旧的外层结构类名 Wrapper 已被 DataFile 取代");
        }

        [Test]
        public void Generate_CustomNamespaceAndPrimaryKey()
        {
            CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut, "MyGame.Data");
            options.ClassName = "CustomTable";
            options.PrimaryKey = "Name_s";
            CExcelGenerateResult result = CExcelGenerator.Generate(_tmpXlsx, options);
            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            string getterText = File.ReadAllText(CExcelTestFactory.CodePath(options, "CustomTable", "CustomTableGetter.cs"));
            StringAssert.Contains("namespace MyGame.Data", getterText);
            StringAssert.Contains("public static CustomTable Get(string key)", getterText, "自定义主键应为 string");
        }

        [Test]
        public void Generate_NoPrimaryKeyColumn_StillGeneratesWithWarning()
        {
            // 只有非主键列（float/string 数组等）：**不该报错不生成**。
            // 以前这里直接失败，于是"没有 ID 的表"完全拿不到数据；现在给警告 + 照常生成，
            // 只是省略按主键的接口（细粒度断言见 CExcelKeyTests）。
            string keyless = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Price_f", 1.5, "Tags_sa", "a;b"),
            }, "coffeebean_bad");
            try
            {
                var options = CExcelTestFactory.TempPackageOptions(_tmpOut);
                var result = CExcelGenerator.Generate(keyless, options);

                Assert.IsTrue(result.Success, string.Join("\n", result.Issues));
                Assert.IsTrue(result.Issues.Exists(i => i.Level == CExcelIssueLevel.Warning && i.Message.Contains("没有可做键的列")),
                    "应给出「没有可做键的列」警告，而不是静默");
                Assert.IsTrue(File.Exists(CExcelTestFactory.CodePath(options, "coffeebean_bad", "coffeebean_badGetter.cs")),
                    "应照常生成 Getter（只是不带主键接口）");
            }
            finally
            {
                CExcelTestFactory.DeleteTempFile(keyless);
            }
        }

        [Test]
        public void Generate_FileMissing_ReturnsError()
        {
            var result = CExcelGenerator.Generate(Path.Combine(Path.GetTempPath(), "missing.xlsx"),
                CExcelTestFactory.TempPackageOptions(_tmpOut));
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

                CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut);
                CExcelGenerateResult result = CExcelGenerator.GenerateFolder(folder, options);

                Assert.IsTrue(result.Success, string.Join("\n", result.Issues));
                Assert.AreEqual(3, result.GeneratedFiles.Count, "只生成正式表（跳过 ~$ 临时表）");
                Assert.IsTrue(File.Exists(CExcelTestFactory.CodePath(options, "A", "A.cs")));
                Assert.IsTrue(File.Exists(CExcelTestFactory.DataPath(options, "A", "A")));
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

        /// <summary>
        /// 测试自己用的反序列化探针（**不是**生成模板里的那个类）。
        /// 生成产物里的外层结构类现在叫 <c>DataFile</c>（旧名 Wrapper），
        /// 这里保留同名局部类是刻意的：探针只关心"数据文件形状 = 一个 data 数组"，
        /// 用测试自己的类型独立验证，避免把"生成代码写错"和"JSON 读不回来"两件事混在一起。
        /// </summary>
        [Serializable]
        public sealed class Wrapper
        {
            public List<Row> data;
        }

        [Serializable]
        public sealed class Row
        {
            public int Id;
            public string Name;
            public float Price;
            public int[] Rewards;
            public bool Enabled;
            public object Gold;
        }
    }
}
