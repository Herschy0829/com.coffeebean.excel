using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>
    /// 枚举生成 / 严格校验的**端到端**测试（Excel → 生成产物）。
    ///
    /// 单元级的枚举规则在 <c>CExcelEnumDefTests</c>；这里只关心"从一张真表走到产物"这条链：
    /// 生成的枚举类型、字段类型、数据容器里的数字、引用模式的类型名，以及
    /// "数据填错必须在生成前被拦下"（以前会安静地写成 0）。
    ///
    /// 0.6.0 起产物在临时包内：代码 <c>&lt;包&gt;/&lt;表&gt;/Code/</c>、数据 <c>&lt;包&gt;/&lt;表&gt;/Data/*.cbcfg</c>。
    /// </summary>
    public class CExcelEnumGenerationTests
    {
        private string _tmpXlsx;
        private string _tmpOut;
        private CExcelGenerateOptions _options;

        [TearDown]
        public void TearDown()
        {
            if (_tmpXlsx != null) CExcelTestFactory.DeleteTempFile(_tmpXlsx);
            if (_tmpOut != null && Directory.Exists(_tmpOut)) Directory.Delete(_tmpOut, true);
            _tmpXlsx = null;
            _tmpOut = null;
            _options = null;
        }

        private CExcelGenerateResult Generate(IDictionary<string, object>[] rows, string sheetName = "Building", string className = null)
        {
            _tmpXlsx = CExcelTestFactory.CreateTempTable(rows, sheetName);
            _tmpOut = Path.Combine(Path.GetTempPath(), "coffeebean_enum_out_" + Guid.NewGuid().ToString("N"));
            _options = CExcelTestFactory.TempPackageOptions(_tmpOut, "Config");
            _options.ClassName = className;
            return CExcelGenerator.Generate(_tmpXlsx, _options);
        }

        /// <summary>读生成的代码文件（tableFolder = 普通表类名 / 章节表章节前缀）。</summary>
        private string Code(string tableFolder, string fileName)
            => File.ReadAllText(CExcelTestFactory.CodePath(_options, tableFolder, fileName));

        /// <summary>读 + 解数据容器，返回 JSON 文本（dataName = 普通表类名 / 章节表 sheet 名）。</summary>
        private string Data(string tableFolder, string dataName)
            => CExcelTestFactory.ReadDataJson(CExcelTestFactory.DataPath(_options, tableFolder, dataName));

        // ========== 生成模式 ==========

        [Test]
        public void EnumColumn_GeneratesEnumType_AndFieldUsesIt()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "State_e", "green"),
                CExcelTestFactory.Row("Id_i", 2, "State_e", "idle"),
                CExcelTestFactory.Row("Id_i", 3, "State_e", "boss_7"),
            }, className: "Building");

            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            string classText = Code("Building", "Building.cs");
            StringAssert.Contains("public enum BuildingState", classText, "枚举类型名 = 类名 + 字段名");
            StringAssert.Contains("Green = 0,", classText);
            StringAssert.Contains("Idle = 1,", classText);
            StringAssert.Contains("Boss = 7,", classText, "boss_7 → 显式值 7");
            StringAssert.Contains("public BuildingState State;", classText, "字段类型必须指向生成的枚举");
        }

        /// <summary>`green` 先出现（0），再 `green_7` → 同名不同值，必须报错（不是悄悄改值）。</summary>
        [Test]
        public void SameMemberTwoValues_FailsGeneration()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "State_e", "green"),
                CExcelTestFactory.Row("Id_i", 2, "State_e", "green_7"),
            }, className: "Building");

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Issues.Exists(i => i.Level == CExcelIssueLevel.Error && i.Message.Contains("两个不同的值")),
                string.Join("\n", result.Issues));
        }

        /// <summary>[Flags] 数组：`;` 拆元素、元素内部再用 `|` 组合位标记。</summary>
        [Test]
        public void FlagsArrayColumn_SplitsElementsThenCombinesBits()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Tags_flagsa", "Cold_1|Hot_2;Cold_1"),
            }, className: "Unit");
            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            StringAssert.Contains("public UnitTags[] Tags;", Code("Unit", "Unit.cs"));
            StringAssert.Contains("\"Tags\":[3,1]", Data("Unit", "Unit"),
                "Cold|Hot = 1|2 = 3，第二个元素是 1");
        }

        [Test]
        public void EnumColumn_JsonStoresNumbers()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "State_e", "green"),
                CExcelTestFactory.Row("Id_i", 2, "State_e", "idle"),
            }, className: "Building");
            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            string json = Data("Building", "Building");
            StringAssert.Contains("\"State\":0", json);
            StringAssert.Contains("\"State\":1", json);
        }

        [Test]
        public void FlagsColumn_GeneratesFlagsEnum_AndJsonStoresBitwiseOr()
        {
            // 用显式值让"按位或"看得出来：Cold=1、Hot=2、Cold|Hot=3
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Tags_flags", "Cold_1"),
                CExcelTestFactory.Row("Id_i", 2, "Tags_flags", "Hot_2"),
                CExcelTestFactory.Row("Id_i", 3, "Tags_flags", "Cold|Hot"),
            }, className: "Unit");
            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            string classText = Code("Unit", "Unit.cs");
            StringAssert.Contains("[System.Flags]", classText);
            StringAssert.Contains("public enum UnitTags", classText);
            StringAssert.Contains("Cold = 1,", classText);
            StringAssert.Contains("Hot = 2,", classText);

            string json = Data("Unit", "Unit");
            StringAssert.Contains("\"Tags\":1", json);
            StringAssert.Contains("\"Tags\":2", json);
            StringAssert.Contains("\"Tags\":3", json, "Cold|Hot = 1|2 = 3");
        }

        [Test]
        public void EnumArrayColumn_UsesArrayFieldAndArrayJson()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "State_ea", "green;idle"),
                CExcelTestFactory.Row("Id_i", 2, "State_ea", "idle"),
            }, className: "Building");
            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            // 类型名 = 类名 + 字段名（字段名 = State_ea → State），数组只在字段声明上加 []
            StringAssert.Contains("public BuildingState[] State;", Code("Building", "Building.cs"));
            StringAssert.Contains("\"State\":[0,1]", Data("Building", "Building"));
        }

        /// <summary>列名冲突防护：不同表的同名枚举类型不能互相踩。</summary>
        [Test]
        public void EnumTypeName_IsNamespacedByTableName()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "State_e", "green"),
            }, className: "Unit");

            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));
            StringAssert.Contains("public enum UnitState", Code("Unit", "Unit.cs"));
            StringAssert.DoesNotContain("BuildingState", Code("Unit", "Unit.cs"));
        }

        // ========== 引用模式 ==========

        [Test]
        public void ReferenceMode_UsesCompiledEnumType_AndGeneratesNoEnum()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Day_e:System.DayOfWeek", "Monday"),
                CExcelTestFactory.Row("Id_i", 2, "Day_e:System.DayOfWeek", "Friday"),
            }, className: "Weekday");
            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            string classText = Code("Weekday", "Weekday.cs");
            StringAssert.Contains("public System.DayOfWeek Day;", classText);
            StringAssert.DoesNotContain("public enum", classText, "引用模式不生成枚举");

            string json = Data("Weekday", "Weekday");
            StringAssert.Contains("\"Day\":1", json);
            StringAssert.Contains("\"Day\":5", json);
        }

        [Test]
        public void ReferenceMode_UnknownMember_FailsGenerationWithUsefulMessage()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Day_e:System.DayOfWeek", "Fryday"),
            }, className: "Weekday");

            Assert.IsFalse(result.Success);
            CExcelIssue error = result.Issues.Find(i => i.Level == CExcelIssueLevel.Error);
            Assert.IsNotNull(error);
            StringAssert.Contains("Fryday", error.Message);
            StringAssert.Contains("Monday", error.Message, "报错要列出可用成员，否则用户还得去翻枚举");
        }

        // ========== 严格校验（以前会安静地写 0） ==========

        [Test]
        public void BadIntCell_FailsGenerationWithRowAndColumn()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Level_i", 10),
                CExcelTestFactory.Row("Id_i", 2, "Level_i", "abc"),
            }, className: "Building");

            Assert.IsFalse(result.Success, "填错的值必须拦下来，不能安静地写成 0");
            CExcelIssue error = result.Issues.Find(i => i.Level == CExcelIssueLevel.Error && i.Column == "Level_i");
            Assert.IsNotNull(error);
            Assert.AreEqual(3, error.Row, "第 2 行数据 = Excel 第 3 行（表头 1 行）");
            StringAssert.Contains("abc", error.Message);
        }

        [Test]
        public void StrictTypeCheckOff_DowngradesToNoError()
        {
            _tmpXlsx = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Level_i", "abc"),
            }, "Building");
            _tmpOut = Path.Combine(Path.GetTempPath(), "coffeebean_lenient_" + Guid.NewGuid().ToString("N"));

            _options = CExcelTestFactory.TempPackageOptions(_tmpOut, "Config");
            _options.ClassName = "Building";
            _options.StrictTypeCheck = false;   // 老表迁移期的逃生门

            CExcelGenerateResult result = CExcelGenerator.Generate(_tmpXlsx, _options);

            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));
            StringAssert.Contains("\"Level\":0", Data("Building", "Building"));
        }

        /// <summary>
        /// `_b` 从 bool 改成 BigInteger 的**迁移提示**：老表里 `Enabled_b` 填 true/false 的地方
        /// 要明确提示改用 `_bool`（否则用户只看到"不是整数"一脸懵）。
        /// </summary>
        [Test]
        public void OldBoolStyleInBColumn_GetsAMigrationWarning()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Enabled_b", "true"),
                CExcelTestFactory.Row("Id_i", 2, "Enabled_b", "false"),
            }, className: "Building");

            Assert.IsTrue(result.Issues.Exists(i => i.Level == CExcelIssueLevel.Warning && i.Message.Contains("_bool")),
                "应提示 `_b` 已改成 BigInteger、布尔要写 `_bool`：" + string.Join("\n", result.Issues));
        }

        [Test]
        public void EmptyCell_IsNotAnError_UsesDefault()
        {
            CExcelGenerateResult result = Generate(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Level_i", 5, "Rate_f", (object)null),
            }, className: "Building");

            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));
            string json = Data("Building", "Building");
            StringAssert.Contains("\"Rate\":0", json);
        }

        // ========== 章节表的枚举并集 ==========

        [Test]
        public void ChapterEnum_UnionsValuesAcrossSheets_IntoOneSharedEnum()
        {
            _tmpXlsx = CExcelTestFactory.CreateMultiSheetTempTable(
                ("StageConfig_1", new[]
                {
                    CExcelTestFactory.Row("Id_i", 1, "State_e", "green"),
                    CExcelTestFactory.Row("Id_i", 2, "State_e", "idle"),
                }),
                ("StageConfig_2", new[]
                {
                    CExcelTestFactory.Row("Id_i", 1, "State_e", "boss"),
                }));
            _tmpOut = Path.Combine(Path.GetTempPath(), "coffeebean_chapter_enum_" + Guid.NewGuid().ToString("N"));

            _options = CExcelTestFactory.TempPackageOptions(_tmpOut, "Config");
            CExcelGenerateResult result = CExcelGenerator.GenerateAllSheets(_tmpXlsx, _options);

            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            // 章节族共用一个目录 <包>/StageConfig/：基类在 Code/、各章节数据在 Data/
            string baseClass = Code("StageConfig", "StageConfigBase.cs");
            StringAssert.Contains("public enum StageConfigState", baseClass, "章节枚举用章节前缀命名，各章节共用一个类型");
            StringAssert.Contains("Green = 0,", baseClass);
            StringAssert.Contains("Idle = 1,", baseClass);
            StringAssert.Contains("Boss = 2,", baseClass, "第 2 章的新取值要并进同一个枚举");
            StringAssert.Contains("public StageConfigState State;", baseClass);
            StringAssert.Contains("public class StageConfigBase", baseClass, "枚举定义所在的章节基类新名是 <前缀>Base");

            // 子类不自带枚举（枚举在基类文件里，只生成一次）；类名新规范是 <前缀>Chapter<N>
            string chapterOne = Code("StageConfig", "StageConfigChapter1.cs");
            StringAssert.Contains("public sealed class StageConfigChapter1 : StageConfigBase", chapterOne);
            StringAssert.DoesNotContain("public enum", chapterOne);

            // 章节独立 Getter 也跟着新命名（旧名 StageConfig_1Getter.cs）
            Assert.IsTrue(File.Exists(CExcelTestFactory.CodePath(_options, "StageConfig", "StageConfigChapter1Getter.cs")),
                "章节独立 Getter 应为 <前缀>Chapter<N>Getter.cs");
            Assert.IsFalse(File.Exists(CExcelTestFactory.CodePath(_options, "StageConfig", "StageConfig_1Getter.cs")),
                "旧名 <前缀>_<N>Getter.cs 不该再产出");

            string chapterTwoJson = Data("StageConfig", "StageConfig_2");
            StringAssert.Contains("\"State\":2", chapterTwoJson, "并集编号要对所有章节一致");
        }
    }
}
