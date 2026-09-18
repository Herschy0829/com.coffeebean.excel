using System.Collections.Generic;
using NUnit.Framework;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>
    /// 类型映射表（<see cref="CExcelTypeCatalog"/>）测试。
    ///
    /// 它是"窗口/文档/解析"三方共用的唯一数据源，所以这里既锁**表本身的完整性**，
    /// 也锁**表和解析代码一致**（后缀能往返、示例真的能按声明的类型解析、后缀之间不歧义）——
    /// 以后加 `_e`、`_b`=BigInteger 时，任何一处对不上都会立刻红。
    /// </summary>
    public class CExcelTypeCatalogTests
    {
        [Test]
        public void EveryKindHasExactlyOneSpec()
        {
            foreach (CExcelFieldKind scalar in new[]
                     {
                         CExcelFieldKind.Int, CExcelFieldKind.Long, CExcelFieldKind.Float,
                         CExcelFieldKind.Double, CExcelFieldKind.Bool, CExcelFieldKind.String,
                     })
            {
                CExcelTypeSpec spec = CExcelTypeCatalog.Find(scalar);
                Assert.IsNotNull(spec, $"{scalar} 在映射表里没有条目");
                Assert.AreEqual(scalar, spec.Kind);
            }
        }

        [Test]
        public void SpecsAreCompleteAndWellFormed()
        {
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                Assert.IsNotEmpty(spec.Suffix, $"{spec.Kind} 缺少标量后缀");
                Assert.IsNotEmpty(spec.ArraySuffix, $"{spec.Kind} 缺少数组后缀");
                Assert.IsNotEmpty(spec.CSharpType, $"{spec.Kind} 缺少 C# 类型");
                Assert.IsNotEmpty(spec.Example, $"{spec.Kind} 缺少单元格示例（窗口要给用户抄）");
                Assert.IsNotEmpty(spec.ArrayExample, $"{spec.Kind} 缺少数组示例");
                Assert.IsNotEmpty(spec.Note, $"{spec.Kind} 缺少说明");

                StringAssert.StartsWith("_", spec.Suffix);
                StringAssert.StartsWith("_", spec.ArraySuffix);
                Assert.AreEqual(spec.Suffix + "a", spec.ArraySuffix, "数组后缀约定 = 标量后缀 + a");
            }
        }

        /// <summary>数组类型的编码必须与 <c>IsArray/ElementKind</c> 的约定一致。</summary>
        [Test]
        public void ArrayKindMatchesInferConvention()
        {
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                Assert.IsTrue(CExcelTypeInfer.IsArray(spec.ArrayKind), $"{spec.ArrayKind} 应被判为数组类型");
                Assert.IsFalse(CExcelTypeInfer.IsArray(spec.Kind), $"{spec.Kind} 不该被判为数组类型");
                Assert.AreEqual(spec.Kind, CExcelTypeInfer.ElementKind(spec.ArrayKind),
                    $"{spec.ArrayKind} 的元素类型应是 {spec.Kind}");
            }
        }

        [Test]
        public void SuffixRoundTripsThroughInfer()
        {
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                Assert.AreEqual(spec.Kind, CExcelTypeInfer.FromSuffix("SomeColumn" + spec.Suffix),
                    $"{spec.Suffix} 应解析为 {spec.Kind}");
                Assert.AreEqual(spec.ArrayKind, CExcelTypeInfer.FromSuffix("SomeColumn" + spec.ArraySuffix),
                    $"{spec.ArraySuffix} 应解析为 {spec.ArrayKind}");
            }
        }

        [Test]
        public void CSharpTypeMatchesSpec()
        {
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                Assert.AreEqual(spec.CSharpType, CExcelTypeInfer.CSharpType(spec.Kind));
                Assert.AreEqual(spec.CSharpType + "[]", CExcelTypeInfer.CSharpType(spec.ArrayKind));
            }
        }

        [Test]
        public void NoDuplicateSuffixes()
        {
            var seen = new HashSet<string>();
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                Assert.IsTrue(seen.Add(spec.Suffix), $"标量后缀重复：{spec.Suffix}");
                Assert.IsTrue(seen.Add(spec.ArraySuffix), $"数组后缀重复：{spec.ArraySuffix}");
            }
        }

        /// <summary>
        /// **歧义防护**：任何后缀都不能是另一个后缀的结尾 ——
        /// 否则 `Count_ul` 这类列名会被 `_l` 抢先命中。加新后缀（如 `_bool` vs `_b`）时必须过这一关。
        /// </summary>
        [Test]
        public void NoSuffixIsAnEndingOfAnother()
        {
            var all = new List<string>();
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                all.Add(spec.Suffix);
                all.Add(spec.ArraySuffix);
            }

            for (int i = 0; i < all.Count; i++)
            {
                for (int j = 0; j < all.Count; j++)
                {
                    if (i == j) continue;
                    Assert.IsFalse(all[i].EndsWith(all[j], System.StringComparison.Ordinal),
                        $"后缀 {all[j]} 是 {all[i]} 的结尾 —— Union 时会互相抢匹配（列名 {('X' + all[i])} 可能被解析成别的类型）");
                }
            }
        }

        /// <summary>
        /// 窗口里给的示例必须**真的能按声明的类型解析**（文档不能是编的）。
        /// </summary>
        [Test]
        public void ExamplesActuallyInferToTheirDeclaredKind()
        {
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                string[] samples = new[] { spec.Example };
                foreach (string sample in samples)
                {
                    CExcelFieldKind inferred = CExcelTypeInfer.Infer("Plain", new object[] { sample });
                    switch (spec.Kind)
                    {
                        case CExcelFieldKind.String:
                            // 任意文本推断结果本就可能是 string（示例："任意文本"）
                            break;
                        case CExcelFieldKind.Int:
                            Assert.AreEqual(CExcelFieldKind.Int, inferred, $"示例 \"{sample}\" 推不出 int");
                            break;
                        case CExcelFieldKind.Long:
                            Assert.AreEqual(CExcelFieldKind.Long, inferred, $"示例 \"{sample}\" 推不出 long");
                            break;
                        case CExcelFieldKind.Double:
                            Assert.AreEqual(CExcelFieldKind.Double, inferred, $"示例 \"{sample}\" 推不出 double");
                            break;
                        case CExcelFieldKind.Float:
                            // float 在无后缀推断里会落到 double（推断没有 float 档），只要求是数值
                            Assert.IsTrue(inferred == CExcelFieldKind.Double || inferred == CExcelFieldKind.Int,
                                $"示例 \"{sample}\" 应是数值");
                            break;
                        case CExcelFieldKind.Bool:
                            Assert.AreEqual(CExcelFieldKind.Bool, inferred, $"示例 \"{sample}\" 推不出 bool");
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// 显式锁住"无后缀推断的优先级"，因为文档一度写错：
        /// **int 优先于 bool** —— 纯 1/0 的列无后缀时是 int，不是 bool；想要 bool 必须写 `_b`。
        /// 这条以前只写在注释里，现在既是文档（窗口会显示）也是测试。
        /// </summary>
        [Test]
        public void BoolLiterals_OneZero_InferToIntUnlessSuffixSaysBool()
        {
            Assert.AreEqual(CExcelFieldKind.Int, CExcelTypeInfer.Infer("Flag", new object[] { "1", "0" }),
                "无后缀 + 纯 1/0 → int（int 优先于 bool）");
            Assert.AreEqual(CExcelFieldKind.Bool, CExcelTypeInfer.Infer("Flag", new object[] { "true", "false" }),
                "无后缀 + true/false → bool");
            Assert.AreEqual(CExcelFieldKind.Bool, CExcelTypeInfer.Infer("Flag_b", new object[] { "1", "0" }),
                "写了 _b 就以后缀为准 → bool");
            Assert.AreEqual(CExcelFieldKind.Bool, CExcelTypeInfer.FromSuffix("Flag_b"),
                "四种字面量在后缀声明下都算 bool");
        }

        [Test]
        public void SuffixLengthComesFromTheTable_NotHardcoded()
        {
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                Assert.AreEqual(spec.Suffix.Length, CExcelTypeCatalog.SuffixLength(spec.Kind));
                Assert.AreEqual(spec.ArraySuffix.Length, CExcelTypeCatalog.SuffixLength(spec.ArrayKind));
            }
        }

        [Test]
        public void FieldNameStripsTheCatalogSuffix()
        {
            Assert.AreEqual("Level", CExcelTypeInfer.ToFieldName("Level_i"));
            Assert.AreEqual("RewardItems", CExcelTypeInfer.ToFieldName("reward_items_sa"));
        }

        // ========== 规划中 ==========

        [Test]
        public void PlannedTypes_AreDocumentedButNotAccidentallySupported()
        {
            foreach (CExcelPlannedType planned in CExcelTypeCatalog.PlannedTypes)
            {
                Assert.IsNotEmpty(planned.Suffix);
                Assert.IsNotEmpty(planned.CSharpType);
                Assert.IsNotEmpty(planned.Example, $"{planned.Suffix} 要给示例");
                Assert.IsNotEmpty(planned.Blocker, $"{planned.Suffix} 要写明为什么现在不能用");
            }

            // `_b(bool)` 目前仍是**已实现**的；等它改成 BigInteger 时这条会红，提醒同步窗口与文档
            Assert.AreEqual(CExcelFieldKind.Bool, CExcelTypeInfer.FromSuffix("Enabled_b"));
        }

        /// <summary>窗口导出的纯文本也得包含全部条目（贴文档用）。</summary>
        [Test]
        public void PlainTextExportCoversEverySuffix()
        {
            string text = CExcelTypeMappingWindow.ToPlainText();
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                StringAssert.Contains(spec.Suffix, text);
                StringAssert.Contains(spec.ArraySuffix, text);
            }
            foreach (CExcelPlannedType planned in CExcelTypeCatalog.PlannedTypes)
            {
                StringAssert.Contains(planned.Suffix, text);
            }
        }
    }
}
