using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>CExcelReader 读取测试：基本读取 / 表头检测 / 别名 / 跳过 / 错误分级。</summary>
    public class CExcelReaderTests
    {
        private string _tmpFile;

        [TearDown]
        public void TearDown()
        {
            CExcelTestFactory.DeleteTempFile(_tmpFile);
            _tmpFile = null;
        }

        /// <summary>造一张单行表头表（key = 列名，value = 单元格值）。</summary>
        private string CreateTable(params Dictionary<string, object>[] rows)
        {
            _tmpFile = CExcelTestFactory.CreateTempTable(rows);
            return _tmpFile;
        }

        [Test]
        public void Read_BasicRowsAndColumns()
        {
            string path = CreateTable(
                CExcelTestFactory.Row("Id_i", 1, "Name_s", "苹果", "Price_f", 1.5),
                CExcelTestFactory.Row("Id_i", 2, "Name_s", "香蕉", "Price_f", 2.5));

            CExcelReadResult result = CExcelReader.Read(path);

            Assert.IsFalse(result.HasBlockingErrors);
            Assert.AreEqual(2, result.Rows.Count);
            CollectionAssert.AreEqual(new[] { "Id_i", "Name_s", "Price_f" }, result.Columns);
            Assert.AreEqual(1, result.Rows[0]["Id_i"]);
            Assert.AreEqual("苹果", result.Rows[0]["Name_s"]);
            Assert.AreEqual(1.5, result.Rows[0]["Price_f"]);
        }

        [Test]
        public void HeaderDetection_PrefersSuffixedRow()
        {
            // 表头检测（内部方法）：伪行集合——中文说明行（无后缀）在前、字段名行（后缀）在后
            var rows = new List<IDictionary<string, object>>
            {
                new Dictionary<string, object> { ["A"] = "商品ID", ["B"] = "名称", ["C"] = "价格" },
                new Dictionary<string, object> { ["A"] = "Id_i", ["B"] = "Name_s", ["C"] = "Price_f" },
            };

            int index = CExcelReader.FindHeaderRow(rows, new CExcelReadOptions());

            Assert.AreEqual(1, index, "应选中带类型后缀的字段名行");
        }

        [Test]
        public void HeaderDetection_FallsBackToFirstRow_WhenNoneSuffixed()
        {
            var rows = new List<IDictionary<string, object>>
            {
                new Dictionary<string, object> { ["A"] = "名称", ["B"] = "价格" },
                new Dictionary<string, object> { ["A"] = "a", ["B"] = "1.5" },
            };

            int index = CExcelReader.FindHeaderRow(rows, new CExcelReadOptions());

            Assert.AreEqual(0, index, "无后缀行时回退到第 1 行");
        }

        [Test]
        public void Read_ColumnAliases_Mapped()
        {
            var options = new CExcelReadOptions
            {
                ColumnAliases = new Dictionary<string, string[]>
                {
                    ["Id_i"] = new[] { "Id_i", "ID_i", "商品ID" },
                    ["Name_s"] = new[] { "Name_s", "名称" },
                },
            };
            string path = CreateTable(
                CExcelTestFactory.Row("商品ID", 7, "名称", "咖啡"));

            CExcelReadResult result = CExcelReader.Read(path, options);

            Assert.IsFalse(result.HasBlockingErrors);
            CollectionAssert.AreEqual(new[] { "Id_i", "Name_s" }, result.Columns, "中文表头应映射为规范列名");
            Assert.AreEqual(7, result.Rows[0]["Id_i"]);
        }

        [Test]
        public void Read_SkipsRowsWithNullOnly()
        {
            // SaveAs 的字典键固定为表头列；"空行"以全 null 值模拟，"注释行"以首列 # 开头模拟
            string path = CreateTable(
                CExcelTestFactory.Row("Id_i", 1, "Name_s", "A"),
                CExcelTestFactory.Row("Id_i", null, "Name_s", null),
                CExcelTestFactory.Row("Id_i", "# 注释说明行", "Name_s", null),
                CExcelTestFactory.Row("Id_i", 2, "Name_s", "B"));

            CExcelReadResult result = CExcelReader.Read(path);

            Assert.AreEqual(2, result.Rows.Count, "空行与注释行应被跳过");
            Assert.AreEqual(1, result.Rows[0]["Id_i"]);
            Assert.AreEqual(2, result.Rows[1]["Id_i"]);
        }

        [Test]
        public void Read_FileMissing_ReturnsError()
        {
            CExcelReadResult result = CExcelReader.Read(Path.Combine(Path.GetTempPath(), "no_such_file.xlsx"));
            Assert.IsTrue(result.HasBlockingErrors);
            StringAssert.Contains("不存在", result.Issues[0].Message);
        }

        [Test]
        public void Read_StrictSuffixOff_AcceptsPlainHeaders()
        {
            var options = new CExcelReadOptions { StrictSuffix = false };
            string path = CreateTable(
                CExcelTestFactory.Row("名称", "A", "价格", 1.5));

            CExcelReadResult result = CExcelReader.Read(path, options);

            Assert.IsFalse(result.HasBlockingErrors, "关闭严格后缀后可接受普通表头");
            CollectionAssert.AreEqual(new[] { "名称", "价格" }, result.Columns);
        }
    }
}
