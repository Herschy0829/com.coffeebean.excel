using System.Collections.Generic;
using NUnit.Framework;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>CExcelTypeInfer 类型推断测试：后缀表 / 无后缀推断 / 字段名 / 数组 / 枚举引用语法。</summary>
    public class CExcelTypeInferTests
    {
        [Test]
        public void FromSuffix_ScalarKinds()
        {
            Assert.AreEqual(CExcelFieldKind.Int, CExcelTypeInfer.FromSuffix("Id_i"));
            Assert.AreEqual(CExcelFieldKind.Long, CExcelTypeInfer.FromSuffix("Score_l"));
            Assert.AreEqual(CExcelFieldKind.Float, CExcelTypeInfer.FromSuffix("Price_f"));
            Assert.AreEqual(CExcelFieldKind.Double, CExcelTypeInfer.FromSuffix("Weight_d"));
            Assert.AreEqual(CExcelFieldKind.Bool, CExcelTypeInfer.FromSuffix("Enabled_bool"));
            Assert.AreEqual(CExcelFieldKind.String, CExcelTypeInfer.FromSuffix("Name_s"));
            Assert.IsNull(CExcelTypeInfer.FromSuffix("PlainName"), "无后缀应为 null");
        }

        /// <summary>
        /// 短后缀 vs 长后缀的"抢匹配"防护：`_u`/`_ul`/`_us`、`_b`/`_by`/`_bool`、`_s`/`_sb`/`_sh`/`_span`。
        /// 这些是加类型时最容易踩的坑，逐个钉死。
        /// </summary>
        [Test]
        public void FromSuffix_ShortVersusLongSuffixes_DoNotCollide()
        {
            Assert.AreEqual(CExcelFieldKind.UInt, CExcelTypeInfer.FromSuffix("Uid_u"));
            Assert.AreEqual(CExcelFieldKind.ULong, CExcelTypeInfer.FromSuffix("Hash_ul"));
            Assert.AreEqual(CExcelFieldKind.UShort, CExcelTypeInfer.FromSuffix("Hp_us"));

            Assert.AreEqual(CExcelFieldKind.BigInt, CExcelTypeInfer.FromSuffix("Gold_b"));
            Assert.AreEqual(CExcelFieldKind.Byte, CExcelTypeInfer.FromSuffix("Quality_by"));
            Assert.AreEqual(CExcelFieldKind.Bool, CExcelTypeInfer.FromSuffix("Flag_bool"));

            Assert.AreEqual(CExcelFieldKind.String, CExcelTypeInfer.FromSuffix("Name_s"));
            Assert.AreEqual(CExcelFieldKind.SByte, CExcelTypeInfer.FromSuffix("Off_sb"));
            Assert.AreEqual(CExcelFieldKind.Short, CExcelTypeInfer.FromSuffix("Delta_sh"));
            Assert.AreEqual(CExcelFieldKind.TimeSpan, CExcelTypeInfer.FromSuffix("Cd_span"));

            Assert.AreEqual(CExcelFieldKind.Decimal, CExcelTypeInfer.FromSuffix("Price_dec"));
            Assert.AreEqual(CExcelFieldKind.Vector4, CExcelTypeInfer.FromSuffix("V_v4"));
        }

        [Test]
        public void FromSuffix_ArrayKinds()
        {
            Assert.AreEqual(CExcelFieldKind.IntArray, CExcelTypeInfer.FromSuffix("Rewards_ia"));
            Assert.AreEqual(CExcelFieldKind.LongArray, CExcelTypeInfer.FromSuffix("Ids_la"));
            Assert.AreEqual(CExcelFieldKind.FloatArray, CExcelTypeInfer.FromSuffix("Weights_fa"));
            Assert.AreEqual(CExcelFieldKind.DoubleArray, CExcelTypeInfer.FromSuffix("Values_da"));
            Assert.AreEqual(CExcelFieldKind.BoolArray, CExcelTypeInfer.FromSuffix("Flags_boola"));
            Assert.AreEqual(CExcelFieldKind.StringArray, CExcelTypeInfer.FromSuffix("Tags_sa"));
            Assert.AreEqual(CExcelFieldKind.BigIntArray, CExcelTypeInfer.FromSuffix("Costs_ba"));
            Assert.AreEqual(CExcelFieldKind.Vector3Array, CExcelTypeInfer.FromSuffix("Path_v3a"));
            Assert.AreEqual(CExcelFieldKind.DictionaryArray, CExcelTypeInfer.FromSuffix("Steps_kva"));
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

        /// <summary>
        /// 无后缀推断**故意不猜 BigInteger**：超 long 的数字串一律当 string，避免悄悄改变老表的字段类型。
        /// 想要大整数就显式写 `_b`。
        /// </summary>
        [Test]
        public void Infer_NoSuffix_HugeDigits_StayString()
        {
            Assert.AreEqual(CExcelFieldKind.String,
                CExcelTypeInfer.Infer("Huge", new object[] { "123456789012345678901234567890" }));
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
            Assert.AreEqual("Path", CExcelTypeInfer.ToFieldName("Path_v3a"));
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
            Assert.AreEqual("System.Numerics.BigInteger", CExcelTypeInfer.CSharpType(CExcelFieldKind.BigInt));
            Assert.AreEqual("decimal", CExcelTypeInfer.CSharpType(CExcelFieldKind.Decimal));
            Assert.AreEqual("System.DateTime", CExcelTypeInfer.CSharpType(CExcelFieldKind.DateTime));
            Assert.AreEqual("UnityEngine.Vector3", CExcelTypeInfer.CSharpType(CExcelFieldKind.Vector3));
            Assert.AreEqual("Dictionary<string,string>", CExcelTypeInfer.CSharpType(CExcelFieldKind.Dictionary));
            Assert.AreEqual("int[]", CExcelTypeInfer.CSharpType(CExcelFieldKind.IntArray));
            Assert.AreEqual("string[]", CExcelTypeInfer.CSharpType(CExcelFieldKind.StringArray));
            Assert.AreEqual("System.Numerics.BigInteger[]", CExcelTypeInfer.CSharpType(CExcelFieldKind.BigIntArray));

            // 枚举族没有固定类型名：返回 null，逼调用方去查表/枚举定义
            Assert.IsNull(CExcelTypeInfer.CSharpType(CExcelFieldKind.Enum));
            Assert.IsNull(CExcelTypeInfer.CSharpType(CExcelFieldKind.EnumArray));
            Assert.IsNull(CExcelTypeInfer.CSharpType(CExcelFieldKind.Flags));
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

        /// <summary>字典数组用 | 分组（组内 ; 是键值对分隔），不能复用数组分隔符。</summary>
        [Test]
        public void SplitGroupValue_UsesPipe()
        {
            CollectionAssert.AreEqual(new[] { "atk=1;hp=2", "atk=3" }, CExcelTypeInfer.SplitGroupValue("atk=1;hp=2|atk=3"));
            CollectionAssert.AreEqual(new[] { "a=1", "b=2" }, CExcelTypeInfer.SplitGroupValue("a=1｜b=2"));
        }

        [Test]
        public void IsSuffixed_Detects()
        {
            Assert.IsTrue(CExcelTypeInfer.IsSuffixed("Price_f"));
            Assert.IsTrue(CExcelTypeInfer.IsSuffixed("Tags_sa"));
            Assert.IsTrue(CExcelTypeInfer.IsSuffixed("State_e:BuildingState"));
            Assert.IsFalse(CExcelTypeInfer.IsSuffixed("Plain"));
            Assert.IsFalse(CExcelTypeInfer.IsSuffixed(null));
        }

        [Test]
        public void IsEnumKind_IdentifiesEnumFamily()
        {
            Assert.IsTrue(CExcelTypeInfer.IsEnumKind(CExcelFieldKind.Enum));
            Assert.IsTrue(CExcelTypeInfer.IsEnumKind(CExcelFieldKind.EnumArray));
            Assert.IsTrue(CExcelTypeInfer.IsEnumKind(CExcelFieldKind.Flags));
            Assert.IsTrue(CExcelTypeInfer.IsEnumKind(CExcelFieldKind.FlagsArray));
            Assert.IsFalse(CExcelTypeInfer.IsEnumKind(CExcelFieldKind.Int));
            Assert.IsFalse(CExcelTypeInfer.IsEnumKind(CExcelFieldKind.IntArray));

            Assert.AreEqual(CExcelFieldKind.Enum, CExcelTypeInfer.EnumElementKind(CExcelFieldKind.EnumArray));
            Assert.AreEqual(CExcelFieldKind.Flags, CExcelTypeInfer.EnumElementKind(CExcelFieldKind.Flags));
        }
    }
}
