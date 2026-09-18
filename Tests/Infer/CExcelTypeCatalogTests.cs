using System.Collections.Generic;
using NUnit.Framework;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>
    /// 类型映射表（<see cref="CExcelTypeCatalog"/>）测试。
    ///
    /// 它是"窗口/文档/解析/生成"四方共用的唯一数据源，所以这里既锁**表本身的完整性**，
    /// 也锁**表和各处代码一致**：每个 kind 恰好一条、后缀无歧义、示例真能生成合法 JSON、
    /// 数组布局与 <c>IsArray/ElementKind</c> 的约定一致 —— 以后加类型时任何一处漏了都会立刻红。
    /// </summary>
    public class CExcelTypeCatalogTests
    {
        /// <summary>每个标量 kind 都必须有且只有一条说明（靠枚举遍历，加类型时忘写表会红）。</summary>
        [Test]
        public void EveryScalarKindHasExactlyOneSpec()
        {
            var seen = new HashSet<CExcelFieldKind>();
            for (int i = 0; i < CExcelTypeInfer.ScalarCount; i++)
            {
                var kind = (CExcelFieldKind)i;
                CExcelTypeSpec spec = CExcelTypeCatalog.Find(kind);
                Assert.IsNotNull(spec, $"{kind} 在映射表里没有条目 —— 加类型时请在 CExcelTypeCatalog.Rows 里补一行");
                Assert.AreEqual(kind, spec.Kind);
                Assert.IsTrue(seen.Add(kind), $"{kind} 有重复条目");
            }
            Assert.AreEqual(CExcelTypeInfer.ScalarCount, CExcelTypeCatalog.All.Count);
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
                Assert.IsNotEmpty(spec.Group, $"{spec.Kind} 缺少分组（窗口按组分块）");
                Assert.IsNotEmpty(spec.ColumnExample, $"{spec.Kind} 缺少字段名示例");

                StringAssert.StartsWith("_", spec.Suffix);
                StringAssert.StartsWith("_", spec.ArraySuffix);
                Assert.AreEqual(spec.Suffix + "a", spec.ArraySuffix, "数组后缀约定 = 标量后缀 + a");
            }
        }

        /// <summary>数组类型的编码必须与 <c>IsArray/ElementKind</c> 的约定一致（块偏移 = 标量个数）。</summary>
        [Test]
        public void ArrayKindMatchesInferConvention()
        {
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                Assert.IsTrue(CExcelTypeInfer.IsArray(spec.ArrayKind), $"{spec.ArrayKind} 应被判为数组类型");
                Assert.IsFalse(CExcelTypeInfer.IsArray(spec.Kind), $"{spec.Kind} 不该被判为数组类型");
                Assert.AreEqual(spec.Kind, CExcelTypeInfer.ElementKind(spec.ArrayKind),
                    $"{spec.ArrayKind} 的元素类型应是 {spec.Kind}");
                Assert.AreEqual(CExcelTypeInfer.ScalarCount, (int)spec.ArrayKind - (int)spec.Kind,
                    "数组块偏移必须等于标量个数（枚举布局约定）");
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
                if (CExcelTypeInfer.IsEnumKind(spec.Kind))
                {
                    Assert.IsNull(CExcelTypeInfer.CSharpType(spec.Kind),
                        "枚举族没有固定类型名（类型名来自表/枚举定义），CSharpType 必须返回 null，逼调用方去查表");
                    Assert.IsNotNull(CExcelTypeInfer.DescribeType("State_e", spec.Kind));
                    Assert.IsNotNull(CExcelTypeInfer.DescribeType("State_ea", spec.ArrayKind));
                    continue;
                }
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
        /// 否则 `Count_ul` 这类列名会被 `_l` 抢先命中。
        /// 加新后缀（比如 `_b`(BigInteger) vs `_bool`、`_u` vs `_ul`）时必须过这一关。
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
                        $"后缀 {all[j]} 是 {all[i]} 的结尾 —— Union 时会互相抢匹配（列名 X{all[i]} 可能被解析成别的类型）");
                }
            }
        }

        /// <summary>
        /// 窗口/文档里给的示例必须**真的能生成合法 JSON**（文档不能是编的）。
        /// 类型层面的"能不能读回"由 <c>CExcelJsonBackendTests</c> 用真反序列化锁。
        /// </summary>
        [Test]
        public void ExamplesAreActuallyValidCellValues()
        {
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                string scalarError = CExcelCellJson.Check(spec.Example, spec.Kind, TestEnum(spec.Kind));
                Assert.IsNull(scalarError, $"{spec.Suffix} 的示例 \"{spec.Example}\" 不合法：{scalarError}");

                string arrayError = CExcelCellJson.Check(spec.ArrayExample, spec.ArrayKind, TestEnum(spec.Kind));
                Assert.IsNull(arrayError, $"{spec.ArraySuffix} 的示例 \"{spec.ArrayExample}\" 不合法：{arrayError}");
            }
        }

        /// <summary>枚举族的示例需要一个枚举定义才能校验（这里给一个"能认下所有示例取值"的假定义）。</summary>
        private static CExcelEnumDef TestEnum(CExcelFieldKind kind)
        {
            if (!CExcelTypeInfer.IsEnumKind(kind)) return null;
            var def = new CExcelEnumDef { TypeName = "TestEnum", Column = "Test", IsFlags = CExcelTypeInfer.EnumElementKind(kind) == CExcelFieldKind.Flags };
            foreach (string[] member in new[] { new[] { "green", "0" }, new[] { "idle", "1" }, new[] { "Fire", "1" }, new[] { "Ice", "2" }, new[] { "Poison", "4" } })
            {
                def.Members.Add(new CExcelEnumMember { RawName = member[0], Name = member[0], Value = long.Parse(member[1]) });
            }
            return def;
        }

        /// <summary>
        /// 显式锁住"无后缀推断的优先级"：**int 优先于 bool** ——
        /// 纯 1/0 的列无后缀时是 int，不是 bool；想要 bool 必须写 `_bool`。
        /// </summary>
        [Test]
        public void BoolLiterals_OneZero_InferToIntUnlessSuffixSaysBool()
        {
            Assert.AreEqual(CExcelFieldKind.Int, CExcelTypeInfer.Infer("Flag", new object[] { "1", "0" }),
                "无后缀 + 纯 1/0 → int（int 优先于 bool）");
            Assert.AreEqual(CExcelFieldKind.Bool, CExcelTypeInfer.Infer("Flag", new object[] { "true", "false" }),
                "无后缀 + true/false → bool");
            Assert.AreEqual(CExcelFieldKind.Bool, CExcelTypeInfer.FromSuffix("Flag_bool"),
                "写了 _bool 就以后缀为准 → bool");
        }

        /// <summary>
        /// `_b` 从 bool 改成 BigInteger 是**破坏性变更** —— 这条测试就是那份"迁移公告"：
        /// `_b` = 大整数、`_bool` = 布尔，两个后缀都不能少。
        /// </summary>
        [Test]
        public void BigIntTookOverTheBSuffix_AndBoolMovedToBool()
        {
            Assert.AreEqual(CExcelFieldKind.BigInt, CExcelTypeInfer.FromSuffix("Enabled_b"));
            Assert.AreEqual(CExcelFieldKind.BigIntArray, CExcelTypeInfer.FromSuffix("Values_ba"));
            Assert.AreEqual(CExcelFieldKind.Bool, CExcelTypeInfer.FromSuffix("Enabled_bool"));
            Assert.AreEqual(CExcelFieldKind.BoolArray, CExcelTypeInfer.FromSuffix("Flags_boola"));
            Assert.AreEqual("System.Numerics.BigInteger", CExcelTypeInfer.CSharpType(CExcelFieldKind.BigInt));
            Assert.AreEqual("bool", CExcelTypeInfer.CSharpType(CExcelFieldKind.Bool));
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
            Assert.AreEqual("Gold", CExcelTypeInfer.ToFieldName("Gold_b"));
            Assert.AreEqual("Enabled", CExcelTypeInfer.ToFieldName("Enabled_bool"));
            Assert.AreEqual("State", CExcelTypeInfer.ToFieldName("State_e"));
            // 引用型枚举：`:`后面的类型名不属于字段名
            Assert.AreEqual("State", CExcelTypeInfer.ToFieldName("State_e:BuildingState"));
            Assert.AreEqual("State", CExcelTypeInfer.ToFieldName("State_ea:BuildingState"));
        }

        // ========== 引用型枚举的列名解析 ==========

        [Test]
        public void EnumTypeReference_ParsesOnlyOnEnumSuffixes()
        {
            Assert.AreEqual("BuildingState", CExcelTypeInfer.EnumTypeName("State_e:BuildingState"));
            Assert.AreEqual("My.Ns.Flags", CExcelTypeInfer.EnumTypeName("Tags_flags:My.Ns.Flags"));
            Assert.IsNull(CExcelTypeInfer.EnumTypeName("State_e"), "没有冒号 → 生成模式");
            Assert.IsNull(CExcelTypeInfer.EnumTypeName("Name_s:weird"), "非枚举后缀的冒号不算类型名");

            Assert.AreEqual(CExcelFieldKind.Enum, CExcelTypeInfer.FromSuffix("State_e:BuildingState"));
            Assert.AreEqual(CExcelFieldKind.EnumArray, CExcelTypeInfer.FromSuffix("State_ea:BuildingState"));
            Assert.AreEqual(CExcelFieldKind.Flags, CExcelTypeInfer.FromSuffix("Tags_flags:ElementFlags"));
            Assert.IsTrue(CExcelTypeInfer.IsSuffixed("State_e:BuildingState"), "表头检测要能认出这种列名");
        }

        /// <summary>枚举类型名 = 表名前缀 + 字段名。</summary>
        [Test]
        public void GeneratedEnumTypeName_IsTableNamePlusFieldName()
        {
            var issues = new List<CExcelIssue>();
            var cells = new List<CExcelEnumCell> { new CExcelEnumCell("green", 2) };
            CExcelEnumDef def = CExcelEnumBuilder.Build("State_e", CExcelFieldKind.Enum, false, "Building", cells, issues);

            Assert.IsFalse(issues.Exists(i => i.Level == CExcelIssueLevel.Error), string.Join(" | ", issues));
            Assert.AreEqual("BuildingState", def.TypeName);
        }

        // ========== 主键候选 ==========

        [Test]
        public void KeyCandidates_ExcludeFloatsBoolsStructs()
        {
            Assert.IsTrue(CExcelTypeInfer.IsKeyCandidate(CExcelFieldKind.Int));
            Assert.IsTrue(CExcelTypeInfer.IsKeyCandidate(CExcelFieldKind.Long));
            Assert.IsTrue(CExcelTypeInfer.IsKeyCandidate(CExcelFieldKind.String));
            Assert.IsTrue(CExcelTypeInfer.IsKeyCandidate(CExcelFieldKind.BigInt));
            Assert.IsTrue(CExcelTypeInfer.IsKeyCandidate(CExcelFieldKind.Guid));
            Assert.IsFalse(CExcelTypeInfer.IsKeyCandidate(CExcelFieldKind.Float));
            Assert.IsFalse(CExcelTypeInfer.IsKeyCandidate(CExcelFieldKind.Double));
            Assert.IsFalse(CExcelTypeInfer.IsKeyCandidate(CExcelFieldKind.Bool));
            Assert.IsFalse(CExcelTypeInfer.IsKeyCandidate(CExcelFieldKind.Vector3));
            Assert.IsFalse(CExcelTypeInfer.IsKeyCandidate(CExcelFieldKind.Dictionary));
            Assert.IsFalse(CExcelTypeInfer.IsKeyCandidate(CExcelFieldKind.IntArray), "数组不能当键");
        }

        // ========== 规划中 ==========

        [Test]
        public void PlannedTypes_AreDocumentedAndStillUnsupported()
        {
            Assert.IsNotEmpty(CExcelTypeCatalog.PlannedTypes, "规划区不能空 —— 空了我宁可删掉这个区块");
            foreach (CExcelPlannedType planned in CExcelTypeCatalog.PlannedTypes)
            {
                Assert.IsNotEmpty(planned.Suffix);
                Assert.IsNotEmpty(planned.CSharpType);
                Assert.IsNotEmpty(planned.Example, $"{planned.Suffix} 要给示例");
                Assert.IsNotEmpty(planned.Blocker, $"{planned.Suffix} 要写明为什么现在不能用");
            }
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
            // 枚举语法必须在导出的表里（这是最容易忘的一块）
            StringAssert.Contains("_e:", text);
            StringAssert.Contains("_flags", text);
        }
    }
}
