using System.IO;
using NUnit.Framework;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>增量生成状态测试：修改时间对比 / 记录 / 清空。</summary>
    public class CExcelIncrementalGeneratorTests
    {
        private string _tmpFile;

        [SetUp]
        public void SetUp()
        {
            _tmpFile = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Name_s", "A"),
            }, "incremental_test");
            CExcelIncrementalGenerator.Clear(_tmpFile);
        }

        [TearDown]
        public void TearDown()
        {
            CExcelIncrementalGenerator.Clear(_tmpFile);
            CExcelTestFactory.DeleteTempFile(_tmpFile);
        }

        [Test]
        public void IsChanged_NoRecord_ReturnsTrue()
        {
            Assert.IsTrue(CExcelIncrementalGenerator.IsChanged(_tmpFile), "未记录过应视为已变化");
        }

        [Test]
        public void MarkGenerated_ThenUnchanged()
        {
            CExcelIncrementalGenerator.MarkGenerated(_tmpFile);
            Assert.IsFalse(CExcelIncrementalGenerator.IsChanged(_tmpFile), "记录后未修改应视为未变化");
        }

        [Test]
        public void FileModified_DetectedAsChanged()
        {
            CExcelIncrementalGenerator.MarkGenerated(_tmpFile);
            Assert.IsFalse(CExcelIncrementalGenerator.IsChanged(_tmpFile));

            // 修改文件内容（更新时间）
            File.SetLastWriteTimeUtc(_tmpFile, System.DateTime.UtcNow.AddSeconds(2));
            Assert.IsTrue(CExcelIncrementalGenerator.IsChanged(_tmpFile), "修改时间变化应重新生成");
        }

        [Test]
        public void Clear_SingleFile_ForcesRegenerate()
        {
            CExcelIncrementalGenerator.MarkGenerated(_tmpFile);
            CExcelIncrementalGenerator.Clear(_tmpFile);

            Assert.IsTrue(CExcelIncrementalGenerator.IsChanged(_tmpFile), "清空记录后应重新生成");
        }

        [Test]
        public void Clear_All_ForcesRegenerate()
        {
            string other = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Id_i", 1),
            }, "incremental_other");
            try
            {
                CExcelIncrementalGenerator.MarkGenerated(_tmpFile);
                CExcelIncrementalGenerator.MarkGenerated(other);
                CExcelIncrementalGenerator.Clear();

                Assert.IsTrue(CExcelIncrementalGenerator.IsChanged(_tmpFile));
                Assert.IsTrue(CExcelIncrementalGenerator.IsChanged(other));
            }
            finally
            {
                CExcelIncrementalGenerator.Clear(other);
                CExcelTestFactory.DeleteTempFile(other);
            }
        }

        [Test]
        public void IsChanged_MissingFile_ReturnsFalse()
        {
            Assert.IsFalse(CExcelIncrementalGenerator.IsChanged(Path.Combine(Path.GetTempPath(), "missing.xlsx")));
        }
    }
}
