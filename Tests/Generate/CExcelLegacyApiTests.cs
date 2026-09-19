using System;
using System.IO;
using NUnit.Framework;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>
    /// Legacy 风格（<b>默认</b>）生成物：文件 / 类 / 成员名必须与项目既有 <c>*_DataGetter</c> 逐字一致。
    ///
    /// 为什么这条是硬要求：真实工程里已经有 55 个 <c>_DataGetter.cs</c>、139 处调用点
    /// （<c>GetDataByID</c> 147 次、<c>GetArray</c> 66 次、<c>GetDataByIndex</c> 56 次、<c>GetArrayLenth</c> 35 次、
    /// <c>GetDataNullID</c> 34 次、<c>GetDataBySameID</c> 15 次、<c>GetDataBySameIDMaxlev</c> 8 次、<c>GetData</c> 2 次）。
    /// 生成产物要能**直接替换**它们（业务代码一行不改），就必须连笔误层面的名字（<c>GetArrayLenth</c> 少个 g）都一样。
    /// </summary>
    public class CExcelLegacyApiTests
    {
        [Test]
        public void Legacy_NormalTable_UsesProjectNames()
        {
            string tmpXlsx = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Name_s", "新手礼包", "Price_f", 6.5, "Rewards_ia", "100;200"),
                CExcelTestFactory.Row("Id_i", 2, "Name_s", "月卡", "Price_f", 30.0, "Rewards_ia", "500"),
            }, "coffeebean_legacy");
            string tmpOut = Path.Combine(Path.GetTempPath(), "coffeebean_legacy_out_" + Guid.NewGuid().ToString("N"));
            try
            {
                CExcelGenerateOptions options = CExcelTestFactory.LegacyPackageOptions(tmpOut);
                options.ClassName = "TestTable";
                CExcelGenerateResult result = CExcelGenerator.Generate(tmpXlsx, options);
                Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

                // 文件名 = <表>_DataGetter.cs，且不再产出 Modern 的三件套
                string path = CExcelTestFactory.CodePath(options, "TestTable", "TestTable_DataGetter.cs");
                Assert.IsTrue(File.Exists(path), "Legacy 只产出 <表>_DataGetter.cs");
                Assert.IsFalse(File.Exists(CExcelTestFactory.CodePath(options, "TestTable", "TestTable.cs")),
                    "不该再产出 Modern 的数据类文件");
                Assert.IsFalse(File.Exists(CExcelTestFactory.CodePath(options, "TestTable", "TestTableGetter.cs")),
                    "不该再产出 Modern 的 Getter 文件");

                string text = File.ReadAllText(path);
                StringAssert.Contains("public class TestTable_DataGetter", text);
                StringAssert.Contains("public class TestTable_PropertyBase", text);
                StringAssert.Contains("public class TestTable_DataBase", text);

                // <表>_Data.cs：数据子类空壳（项目里每个表都有这个文件），Getter 的静态缓存就是这个类型
                string dataSubPath = CExcelTestFactory.CodePath(options, "TestTable", "TestTable_Data.cs");
                Assert.IsTrue(File.Exists(dataSubPath), "Legacy 还会产出 <表>_Data.cs");
                StringAssert.Contains("public class TestTable_Data : TestTable_DataBase",
                    File.ReadAllText(dataSubPath));
                StringAssert.Contains("private static TestTable_Data m_TestTable_Data;", text);
                StringAssert.Contains("public static TestTable_DataBase GetData()", text);
                StringAssert.Contains("public static TestTable_PropertyBase GetDataByID(int id)", text);
                StringAssert.Contains("public static TestTable_PropertyBase GetDataNullID(int id)", text);
                StringAssert.Contains("public static TestTable_PropertyBase GetDataBySameID(int id, int lev)", text);
                StringAssert.Contains("public static TestTable_PropertyBase GetDataBySameIDMaxlev(int id)", text);
                StringAssert.Contains("public static TestTable_PropertyBase GetDataByIndex(int index)", text);
                StringAssert.Contains("public static TestTable_PropertyBase GetDataNullIndexNull(int index)", text);
                StringAssert.Contains("public static int GetArrayLenth()", text);
                StringAssert.Contains("public static TestTable_PropertyBase[] GetArray()", text);

                // DataBase 的字段与查询方法
                StringAssert.Contains("public TestTable_PropertyBase[] DataArray;", text);
                StringAssert.Contains("public Dictionary<int, TestTable_PropertyBase> DataDictionary", text);
                StringAssert.Contains("public int ArrayLength;", text);
                StringAssert.Contains("public TestTable_PropertyBase GetDataByID(int _id)", text);
                StringAssert.Contains("public TestTable_PropertyBase GetDataNullID(int _id)", text);
                StringAssert.Contains("public TestTable_PropertyBase GetDataByIndex(int _index)", text);
                StringAssert.Contains("public TestTable_PropertyBase GetDataNullIndexNull(int _index)", text);
                StringAssert.Contains("GetIdProptyList()", text);
                StringAssert.Contains("tempList.Add(DataArray[i].Id);", text, "字段名要与项目一致（列 Id_i → 字段 Id）");

                // 项目语义：找不到 ID → LogError + 首行（id<=0）/ 末行；Null 版 → null
                StringAssert.Contains("Debug.LogError(\"表格：TestTable 中找不到ID： \"+ _id);", text);
                StringAssert.Contains("return DataArray[ArrayLength - 1];", text);
                StringAssert.Contains("return null;", text);

                // Legacy 的类在**全局命名空间**（业务代码不加 using 就能用），但加载要能拿到 ConfigTableRuntime
                StringAssert.DoesNotContain("namespace CoffeeBean", text, "Legacy 类与项目一样放在全局命名空间");
                StringAssert.Contains("using CoffeeBean;", text, "用命名空间里的 ConfigTableRuntime 读包内数据");
                StringAssert.DoesNotContain("TestTableGetter", text, "不该残留 Modern 的类名");

                // 仍然要接上运行时（PreloadAll / 热更重载）
                StringAssert.Contains("private sealed class Registration : IConfigTable", text);
                StringAssert.Contains("ConfigTableRuntime.Register(new Registration());", text);
                StringAssert.Contains("ConfigTableRuntime.ReadData(DataPath)", text);
                StringAssert.Contains("public static void Reload()", text);
            }
            finally
            {
                CExcelTestFactory.DeleteTempFile(tmpXlsx);
                if (Directory.Exists(tmpOut)) Directory.Delete(tmpOut, true);
            }
        }

        [Test]
        public void Legacy_ChapterFamily_UsesProjectNamesAndInjectedChapter()
        {
            string tmpXlsx = CExcelTestFactory.CreateMultiSheetTempTable(
                ("StageConfig_1", new[]
                {
                    CExcelTestFactory.Row("Id_i", 1, "Name_s", "第一章", "Lev_i", 1),
                    CExcelTestFactory.Row("Id_i", 1, "Name_s", "第一章二级", "Lev_i", 2),
                }),
                ("StageConfig_2", new[]
                {
                    CExcelTestFactory.Row("Id_i", 1, "Name_s", "第二章", "Lev_i", 1),
                }));
            string tmpOut = Path.Combine(Path.GetTempPath(), "coffeebean_legacy_ch_" + Guid.NewGuid().ToString("N"));
            try
            {
                CExcelGenerateOptions options = CExcelTestFactory.LegacyPackageOptions(tmpOut);
                CExcelGenerateResult result = CExcelGenerator.GenerateAllSheets(tmpXlsx, options);
                Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

                // 每章节一个空壳子类 + 聚合 Getter（与项目 Directory 里 <T>_1_Data.cs / <T>_DataGetter.cs 一致）
                Assert.IsTrue(File.Exists(CExcelTestFactory.CodePath(options, "StageConfig", "StageConfig_1_Data.cs")));
                Assert.IsTrue(File.Exists(CExcelTestFactory.CodePath(options, "StageConfig", "StageConfig_2_Data.cs")));
                string sub = File.ReadAllText(CExcelTestFactory.CodePath(options, "StageConfig", "StageConfig_1_Data.cs"));
                StringAssert.Contains("public class StageConfig_1_Data : StageConfig_DataBase", sub);

                Assert.IsFalse(File.Exists(CExcelTestFactory.CodePath(options, "StageConfig", "StageConfigGetter.cs")),
                    "不该再产出 Modern 的聚合 Getter 文件");

                string text = File.ReadAllText(CExcelTestFactory.CodePath(options, "StageConfig", "StageConfig_DataGetter.cs"));
                StringAssert.Contains("public class StageConfig_DataGetter", text);
                StringAssert.Contains("public class StageConfig_PropertyBase", text);
                StringAssert.Contains("public class StageConfig_DataBase", text);

                // 每章节一个静态缓存（项目做法：M_<表>_<章节>_Data）
                StringAssert.Contains("private static StageConfig_1_Data m_StageConfig_1_Data;", text);
                StringAssert.Contains("private static StageConfig_2_Data m_StageConfig_2_Data;", text);

                // 每个成员都能省略章节号：省略 = 当前章节（注入）
                StringAssert.Contains("public static StageConfig_DataBase GetData(int chapterID = -1)", text);
                StringAssert.Contains("public static StageConfig_PropertyBase GetDataByID(int id, int chapterID = -1)", text);
                StringAssert.Contains("public static StageConfig_PropertyBase GetDataNullID(int id, int chapterID = -1)", text);
                StringAssert.Contains("public static StageConfig_PropertyBase GetDataBySameID(int id, int lev, int chapterID = -1)", text);
                StringAssert.Contains("public static StageConfig_PropertyBase GetDataBySameIDMaxlev(int id, int chapterID = -1)", text);
                StringAssert.Contains("public static StageConfig_PropertyBase GetDataByIndex(int index, int chapterID = -1)", text);
                StringAssert.Contains("public static StageConfig_PropertyBase GetDataNullIndexNull(int index, int chapterID = -1)", text);
                StringAssert.Contains("public static int GetArrayLenth(int chapterID = -1)", text);
                StringAssert.Contains("public static StageConfig_PropertyBase[] GetArray(int chapterID = -1)", text);

                // 方案 B：章节号来自注入（生成包引用不到游戏状态），不是写死 PlayerDataMgr
                StringAssert.Contains("chapterID = ConfigTableRuntime.CurrentChapterId;", text);
                StringAssert.DoesNotContain("PlayerDataMgr", text, "生成代码绝不能引用游戏类型（不同程序集，编译不过）");

                // 没配置的章节 → 警告 + 上一章数据（照抄项目行为）
                StringAssert.Contains("策划没有配置 第", text);
                StringAssert.Contains("默认给上一章节数据", text);
                StringAssert.Contains("return 2;", text, "兜底给最后一章");
            }
            finally
            {
                CExcelTestFactory.DeleteTempFile(tmpXlsx);
                if (Directory.Exists(tmpOut)) Directory.Delete(tmpOut, true);
            }
        }
    }
}
