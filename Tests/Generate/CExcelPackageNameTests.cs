using System;
using System.IO;
using NUnit.Framework;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>
    /// 包名校验测试。
    ///
    /// **为什么值得单独测**：非法包名不是"生成得难看"这种小问题 —— UPM 在解析阶段就直接失败并阻断启动，
    /// 实测报 <c>Folder [.../Packages] contains invalid packages: Package name 'x' is invalid</c>，
    /// **整个 Unity 工程都打不开**（本条就是实际踩出来的：探针包名以下划线开头，dev 工程起不来）。
    /// 所以生成器必须在生成前拦住，而不是留一个打不开的包。
    /// </summary>
    public class CExcelPackageNameTests
    {
        [TestCase("com.coffeebean.config.generated")]
        [TestCase("com.game.configs")]
        [TestCase("a")]
        [TestCase("my-pkg_2.x")]
        [TestCase("com.example2")]
        public void ValidatePackageName_AcceptsLegalNames(string name)
        {
            Assert.IsNull(CExcelGenerator.ValidatePackageName(name), name + " 应被判为合法");
        }

        [TestCase(null, "空")]
        [TestCase("", "空")]
        [TestCase("__cbgen_probe__", "下划线开头（实测会让工程打不开）")]
        [TestCase("_probe", "下划线开头")]
        [TestCase("Com.Example", "含大写")]
        [TestCase("1abc", "数字开头")]
        [TestCase("com..example", "连续点")]
        [TestCase("com.example.", "点结尾")]
        [TestCase("com.example-", "连字符结尾")]
        [TestCase("com example", "空格")]
        [TestCase("com/example", "斜杠")]
        [TestCase("中文包名", "非 ASCII")]
        public void ValidatePackageName_RejectsIllegalNames(string name, string why)
        {
            Assert.IsNotNull(CExcelGenerator.ValidatePackageName(name),
                (name ?? "<null>") + " 应被判为非法：" + why);
        }

        [Test]
        public void Generate_WithIllegalPackageName_AbortsWithoutWritingAnything()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "coffeebean_pkgname_" + Guid.NewGuid().ToString("N"));
            string xlsx = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Name_s", "x"),
            }, "PkgNameProbe");
            try
            {
                var options = new CExcelGenerateOptions
                {
                    CodeFolder = Path.Combine(tmp, "Package"),
                    PackageName = "__invalid__",
                    Namespace = "Config",
                };

                CExcelGenerateResult result = CExcelGenerator.Generate(xlsx, options);

                Assert.IsFalse(result.Success, "非法包名必须让生成失败");
                Assert.IsTrue(result.Issues.Exists(i => i.Level == CExcelIssueLevel.Error && i.Message.Contains("包名非法")),
                    "应报出「包名非法」错误，而不是静默产出");
                Assert.IsFalse(File.Exists(Path.Combine(options.CodeFolder, "package.json")),
                    "不应写出会让 UPM 解析失败的 package.json");
                Assert.IsFalse(File.Exists(Path.Combine(options.CodeFolder, "PkgNameProbe", "Data",
                        "PkgNameProbe" + CExcelDataContainer.Extension)),
                    "非法包名下不应产出数据文件");
            }
            finally
            {
                CExcelTestFactory.DeleteTempFile(xlsx);
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            }
        }
    }
}
