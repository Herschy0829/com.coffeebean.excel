using System.Collections.Generic;
using NUnit.Framework;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>CExcelTypeInfer 类型推断测试：后缀表 / 无后缀推断 / 字段名 / 数组。</summary>
    public class CExcelTypeInferTests
    {
        [Test]
        public void FromSuffix_ScalarKinds()
        {
            Assert.AreEqual(CExcelFieldKind.Int, CExcelTypeInfer.FromSuffix("Id_i"));
            Assert.AreEqual(CExcelFieldKind.Long, CExcelTypeInfer.FromSuffix("Score_l"));
            Assert.AreEqual(CExcelFieldKind.Float, CExcelTypeInfer.FromSuffix("Price_f"));
            Assert.AreEqual(CExcelFieldKind.Double, CExcelTypeInfer.FromSuffix("Weight_d"));
            Assert.AreEqual(CExcelFieldKind.Bool, CExcelTypeInfer.FromSuffix("Enabled_b"));
            Assert.AreEqual(CExcelFieldKind.String, CExcelTypeInfer.FromSuffix("Name_s"));
            Assert.IsNull(CExcelTypeInfer.FromSuffix("PlainName"), "无后缀应为 null");
        }

        [Test]
        public void FromSuffix_ArrayKinds()
        {
            Assert.AreEqual(CExcelFieldKind.IntArray, CExcelTypeInfer.FromSuffix("Rewards_ia"));
            Assert.AreEqual(CExcelFieldKind.LongArray, CExcelTypeInfer.FromSuffix("Ids_la"));
            Assert.AreEqual(CExcelFieldKind.FloatArray, CExcelTypeInfer.FromSuffix("Weights_fa"));
            Assert.AreEqual(CExcelFieldKind.DoubleArray, CExcelTypeInfer.FromSuffix("Values_da"));
            Assert.AreEqual(CExcelFieldKind.BoolArray, CExcelTypeInfer.FromSuffix("Flags_ba"));
            Assert.AreEqual(CExcelFieldKind.StringArray, CExcelTypeInfer.FromSuffix("Tags_sa"));
        }

        [Test]
        public void Infer_SuffixTakesPriority()
        {
            Assert.AreEqual(CExcelFieldKind.String,
                CExcelTypeInfer.Infer("Name_s", new object[] { 1, 2, 3 }), "后缀声明优先于值推断");
        }

        [Test]
        public void Infer_NoSuffix_AllInt()
        {
            Assert.AreEqual(CExcelFieldKind.Int,
                CExcelTypeInfer.Infer("Count", new object[] { 1, 2, 3 }));
        }

        [Test]
        public void Infer_NoSuffix_OutOfIntRange_IsLong()
        {
            Assert.AreEqual(CExcelFieldKind.Long,
                CExcelTypeInfer.Infer("Big", new object[] { 10000000000L, 20000000000L }));
        }

        [Test]
        public void Infer_NoSuffix_WithDecimal_IsDouble()
        {
            Assert.AreEqual(CExcelFieldKind.Double,
                CExcelTypeInfer.Infer("Ratio", new object[] { 1.5, 2.25 }));
        }

        [Test]
        public void Infer_NoSuffix_AllBoolLiterals()
        {
            Assert.AreEqual(CExcelFieldKind.Bool,
                CExcelTypeInfer.Infer("Flag", new object[] { "true", "false", 1, 0 }));
        }

        [Test]
        public void Infer_NoSuffix_MixedOrEmpty_IsString()
        {
            Assert.AreEqual(CExcelFieldKind.String,
                CExcelTypeInfer.Infer("Mixed", new object[] { 1, "abc", 1.5 }));
            Assert.AreEqual(CExcelFieldKind.String,
                CExcelTypeInfer.Infer("Empty", new object[] { null, null }), "空列推断为 string");
        }

        [Test]
        public void ToFieldName_StripsSuffixAndPascalCases()
        {
            Assert.AreEqual("Id", CExcelTypeInfer.ToFieldName("Id_i"));
            Assert.AreEqual("GoogleProductId", CExcelTypeInfer.ToFieldName("google_product_id_s"));
            Assert.AreEqual("RewardItems", CExcelTypeInfer.ToFieldName("reward_items_ia"));
            Assert.AreEqual("DisplayName", CExcelTypeInfer.ToFieldName("Display Name_s"));
        }

        [Test]
        public void CSharpType_Maps()
        {
            Assert.AreEqual("int", CExcelTypeInfer.CSharpType(CExcelFieldKind.Int));
            Assert.AreEqual("long", CExcelTypeInfer.CSharpType(CExcelFieldKind.Long));
            Assert.AreEqual("float", CExcelTypeInfer.CSharpType(CExcelFieldKind.Float));
            Assert.AreEqual("double", CExcelTypeInfer.CSharpType(CExcelFieldKind.Double));
            Assert.AreEqual("bool", CExcelTypeInfer.CSharpType(CExcelFieldKind.Bool));
            Assert.AreEqual("string", CExcelTypeInfer.CSharpType(CExcelFieldKind.String));
            Assert.AreEqual("int[]", CExcelTypeInfer.CSharpType(CExcelFieldKind.IntArray));
            Assert.AreEqual("string[]", CExcelTypeInfer.CSharpType(CExcelFieldKind.StringArray));
        }

        [Test]
        public void SplitArrayValue_Separators()
        {
            CollectionAssert.AreEqual(new[] { "1", "2", "3" }, CExcelTypeInfer.SplitArrayValue("1;2;3"));
            CollectionAssert.AreEqual(new[] { "1", "2" }, CExcelTypeInfer.SplitArrayValue("1，2"));
            CollectionAssert.AreEqual(new[] { "a", "b" }, CExcelTypeInfer.SplitArrayValue("a,b"));
            Assert.AreEqual(0, CExcelTypeInfer.SplitArrayValue(null).Count);
            Assert.AreEqual(0, CExcelTypeInfer.SplitArrayValue("  ").Count);
        }

        [Test]
        public void IsSuffixed_Detects()
        {
            Assert.IsTrue(CExcelTypeInfer.IsSuffixed("Price_f"));
            Assert.IsTrue(CExcelTypeInfer.IsSuffixed("Tags_sa"));
            Assert.IsFalse(CExcelTypeInfer.IsSuffixed("Plain"));
            Assert.IsFalse(CExcelTypeInfer.IsSuffixed(null));
        }
    }
}
