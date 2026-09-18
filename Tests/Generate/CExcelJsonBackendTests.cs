using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>
    /// **JSON 后端能力锁定测试**（映射表的"另一半真相"）。
    ///
    /// <see cref="CExcelTypeCatalog"/> 说某类型支持，就必须真的能被读回来。这里对**每一个**
    /// catalog 类型都走一遍真实链路：单元格示例 → <see cref="CExcelCellJson.Literal"/> →
    /// 真实 `JsonConvert.DeserializeObject(json, 真实C#类型)` → 断言值。
    ///
    /// 为什么必须是"真反序列化"而不是断言 JSON 文本：文本对不代表类型对
    /// （枚举的 JSON 必须是数字、Vector 必须是对象、BigInteger 绝对不能是科学计数法）。
    ///
    /// 为什么反序列化走 <see cref="CExcelJsonBackend"/> 而不是直接 `using Newtonsoft.Json`：
    /// **Unity 不给测试程序集自动引用插件 DLL**（`optionalUnityReferences: TestAssemblies` 的 asmdef 拿不到），
    /// 直接引用会 CS0246。见 <see cref="CExcelJsonBackend"/> 注释里的实测记录。
    /// </summary>
    public class CExcelJsonBackendTests
    {
        [Test]
        public void Newtonsoft_IsAvailable()
        {
            Assert.IsTrue(CExcelJsonBackend.IsAvailable,
                "excel 的 package.json 声明了 com.unity.nuget.newtonsoft-json 依赖，这里必须能探测到：" + CExcelJsonBackend.ProbeError);
            Assert.AreNotEqual("?", CExcelJsonBackend.Version);
        }

        /// <summary>每个**非枚举** catalog 类型的标量示例必须真的读得回来。</summary>
        [Test]
        public void EveryCatalogScalarExample_DeserializesIntoItsDeclaredType()
        {
            int checkedCount = 0;
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                if (CExcelTypeInfer.IsEnumKind(spec.Kind)) continue;
                Type type = CExcelJsonProbe.ResolveType(spec.CSharpType);
                Assert.IsNotNull(type, $"{spec.Suffix} 的 C# 类型 \"{spec.CSharpType}\" 没有对应的 Type —— 新加类型时请同步本测试");

                string json = CExcelCellJson.Literal(spec.Example, spec.Kind, null, out string error);
                Assert.IsNull(error, $"{spec.Suffix} 的示例 \"{spec.Example}\" 生成 JSON 失败：{error}");

                object value = null;
                Assert.DoesNotThrow(() => value = CExcelJsonBackend.Deserialize(json, type),
                    $"{spec.Suffix}（{spec.CSharpType}）的 JSON {json} 读不回 {type.Name} —— 说明映射表承诺了后端做不到的类型");
                Assert.IsNotNull(value, $"{spec.Suffix} 反序列化结果不该是 null（json={json}）");
                checkedCount++;
            }
            Assert.Greater(checkedCount, 20, "应该检查了 20 种以上类型");
        }

        /// <summary>数组示例同样要读得回来（数组类型 = 元素类型 + []）。</summary>
        [Test]
        public void EveryCatalogArrayExample_DeserializesIntoItsDeclaredArrayType()
        {
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                if (CExcelTypeInfer.IsEnumKind(spec.Kind)) continue;
                Type arrayType = CExcelJsonProbe.ResolveType(spec.CSharpType).MakeArrayType();

                string json = CExcelCellJson.Literal(spec.ArrayExample, spec.ArrayKind, null, out string error);
                Assert.IsNull(error, $"{spec.ArraySuffix} 的示例 \"{spec.ArrayExample}\" 生成 JSON 失败：{error}");

                object value = null;
                Assert.DoesNotThrow(() => value = CExcelJsonBackend.Deserialize(json, arrayType),
                    $"{spec.ArraySuffix} 的 JSON {json} 读不回 {arrayType.Name}");
                var array = value as Array;
                Assert.IsNotNull(array, $"{spec.ArraySuffix} 应读出数组（json={json}）");
                Assert.Greater(array.Length, 0, $"{spec.ArraySuffix} 的示例不该是空数组（json={json}）");
            }
        }

        /// <summary>**json 的表示形式也被锁住**：容易默默错的地方逐个钉死。</summary>
        [Test]
        public void ValueRepresentations_ArePinnedDown()
        {
            Assert.AreEqual("123456789012345678901234567890",
                CExcelCellJson.Literal("123456789012345678901234567890", CExcelFieldKind.BigInt, null, out _),
                "BigInteger 必须是**裸数字**（绝不能是科学计数法）");

            Assert.AreEqual("1.23", CExcelCellJson.Literal("1.23", CExcelFieldKind.Decimal, null, out _));

            Assert.AreEqual("\"2026-09-18T10:30:00\"",
                CExcelCellJson.Literal("2026-09-18 10:30:00", CExcelFieldKind.DateTime, null, out _),
                "日期统一输出 ISO（Newtonsoft 两种都认，但 ISO 更严谨）");

            Assert.AreEqual("\"01:30:00\"", CExcelCellJson.Literal("01:30:00", CExcelFieldKind.TimeSpan, null, out _));

            Assert.AreEqual("{\"x\":1,\"y\":2,\"z\":3}",
                CExcelCellJson.Literal("1,2,3", CExcelFieldKind.Vector3, null, out _),
                "Unity 结构必须是**对象**（Newtonsoft 不支持从字符串转 Vector3）");

            Assert.AreEqual("{\"x\":0,\"y\":0,\"width\":100,\"height\":50}",
                CExcelCellJson.Literal("0,0,100,50", CExcelFieldKind.Rect, null, out _));

            Assert.AreEqual("{\"atk\":\"10\",\"hp\":\"20\"}",
                CExcelCellJson.Literal("atk=10;hp=20", CExcelFieldKind.Dictionary, null, out _));
        }

        /// <summary>颜色三种写法（十六进制 / 0~255 / 0~1）都要落到同一个 Color 上。</summary>
        [Test]
        public void Color_AllThreeNotations_Agree()
        {
            Color hex = Read<Color>("#FF8800", CExcelFieldKind.Color);
            Color byByte = Read<Color>("255,136,0", CExcelFieldKind.Color);
            Color byFloat = Read<Color>("1,0.5333333,0", CExcelFieldKind.Color);

            Assert.AreEqual(1f, hex.r, 0.01f);
            Assert.AreEqual(136f / 255f, hex.g, 0.01f);
            Assert.AreEqual(0f, hex.b, 0.01f);
            Assert.AreEqual(1f, hex.a, 0.001f, "不给 alpha 时应是不透明");
            Assert.AreEqual(hex.r, byByte.r, 0.01f);
            Assert.AreEqual(hex.g, byByte.g, 0.01f);
            Assert.AreEqual(hex.g, byFloat.g, 0.01f);

            // 短写 #F80 = #FF8800
            Color shortHex = Read<Color>("#F80", CExcelFieldKind.Color);
            Assert.AreEqual(hex.r, shortHex.r, 0.01f);
            Assert.AreEqual(hex.g, shortHex.g, 0.01f);

            // 全 0/1 的整数按 0~1 算（`1,0,0` 是纯红，不是 1/255 的暗红）—— 这条最容易反直觉
            Color pureRed = Read<Color>("1,0,0", CExcelFieldKind.Color);
            Assert.AreEqual(1f, pureRed.r, 0.001f);
        }

        /// <summary>四元数：4 个数 = (x,y,z,w)，3 个数 = 欧拉角。</summary>
        [Test]
        public void Quaternion_FourNumbersAreXYZW_ThreeAreEuler()
        {
            Quaternion identity = Read<Quaternion>("0,0,0,1", CExcelFieldKind.Quaternion);
            Assert.AreEqual(1f, identity.w, 0.0001f);

            Quaternion byEuler = Read<Quaternion>("0,90,0", CExcelFieldKind.Quaternion);
            Quaternion expected = Quaternion.Euler(0, 90, 0);
            Assert.AreEqual(expected.y, byEuler.y, 0.0001f);
            Assert.AreEqual(expected.w, byEuler.w, 0.0001f);
        }

        /// <summary>
        /// **数组分隔符按元素类型区分**（踩过的坑）：向量/颜色/矩形/四元数的元素内部就是逗号，
        /// 所以它们的数组只能用 `;` —— 否则 `1,2` 会被拆成两个标量，静默生成错数据。
        /// </summary>
        [Test]
        public void VectorAndStructArrays_UseSemicolonOnly()
        {
            string json = CExcelCellJson.Literal("1,2;3,4", CExcelFieldKind.Vector2Array, null, out string error);
            Assert.IsNull(error, error);
            Assert.AreEqual("[{\"x\":1,\"y\":2},{\"x\":3,\"y\":4}]", json);

            var values = (Vector2[])CExcelJsonBackend.Deserialize(json, typeof(Vector2[]));
            Assert.AreEqual(2, values.Length);
            Assert.AreEqual(3f, values[1].x, 0.001f);

            // 逗号被当成分量分隔符：`1,2,3` 是"一个三分量向量"，而 Vector2 只要 2 个 → 报错（不是悄悄拆 3 个元素）
            Assert.IsNotNull(CExcelCellJson.Check("1,2,3", CExcelFieldKind.Vector2Array, null),
                "分量个数不对必须报错，不能当成 3 个元素");
        }

        /// <summary>字典数组用 | 分组（组内键值对用 ; 或 ,）。</summary>
        [Test]
        public void DictionaryArray_GroupsWithPipe()
        {
            string json = CExcelCellJson.Literal("atk=1;hp=2|atk=3", CExcelFieldKind.DictionaryArray, null, out string error);
            Assert.IsNull(error, error);
            Assert.AreEqual("[{\"atk\":\"1\",\"hp\":\"2\"},{\"atk\":\"3\"}]", json);

            var values = (Dictionary<string, string>[])CExcelJsonBackend.Deserialize(json, typeof(Dictionary<string, string>[]));
            Assert.AreEqual(2, values.Length);
            Assert.AreEqual("2", values[0]["hp"]);
        }

        /// <summary>大整数变成科学计数法时**必须报错**，不能安静地写个错的数。</summary>
        [Test]
        public void BigInteger_ScientificNotation_IsRejectedNotSilentlyTruncated()
        {
            string error = CExcelCellJson.Check("1.23456789012346E+19", CExcelFieldKind.BigInt, null);
            Assert.IsNotNull(error, "科学计数法不能当大整数（Excel 已经把精度吃掉了，必须报错让人改成文本格式）");
            StringAssert.Contains("文本格式", error, "报错要告诉用户怎么办");
        }

        /// <summary>
        /// 顺带把"科学计数法到底会错成什么"钉下来（Newtonsoft 会算出一个**看起来像**的数字）——
        /// 这就是为什么必须在生成端拦下，而不是指望反序列化报错。
        /// </summary>
        [Test]
        public void BigInteger_ScientificNotation_WouldSilentlyChangeTheValue()
        {
            object lossy = CExcelJsonBackend.Deserialize("1.23456789012346E+19", CExcelJsonProbe.ResolveType("System.Numerics.BigInteger"));
            Assert.AreNotEqual("12345678901234600000", lossy.ToString(),
                "如果这条相等了，说明后端行为变了，可以重新评估生成端是否还要拦科学计数法");
        }

        /// <summary>整数范围外的值也必须报错（以前会安静地写 0）。</summary>
        [Test]
        public void OutOfRangeIntegers_AreRejected()
        {
            Assert.IsNotNull(CExcelCellJson.Check("300", CExcelFieldKind.Byte, null));
            Assert.IsNotNull(CExcelCellJson.Check("-1", CExcelFieldKind.UInt, null));
            Assert.IsNotNull(CExcelCellJson.Check("40000", CExcelFieldKind.Short, null));
            Assert.IsNull(CExcelCellJson.Check("255", CExcelFieldKind.Byte, null));
        }

        /// <summary>非法值一律给得出人话错误，不是抛异常。</summary>
        [Test]
        public void InvalidCells_ReportReadableErrors()
        {
            Assert.IsNotNull(CExcelCellJson.Check("abc", CExcelFieldKind.Int, null));
            Assert.IsNotNull(CExcelCellJson.Check("maybe", CExcelFieldKind.Bool, null));
            Assert.IsNotNull(CExcelCellJson.Check("AB", CExcelFieldKind.Char, null));
            Assert.IsNotNull(CExcelCellJson.Check("not-a-date", CExcelFieldKind.DateTime, null));
            Assert.IsNotNull(CExcelCellJson.Check("not-a-guid", CExcelFieldKind.Guid, null));
            Assert.IsNotNull(CExcelCellJson.Check("1,2", CExcelFieldKind.Vector3, null), "向量分量个数不对要报错");
            Assert.IsNotNull(CExcelCellJson.Check("#GGGGGG", CExcelFieldKind.Color, null));
            Assert.IsNotNull(CExcelCellJson.Check("atk10", CExcelFieldKind.Dictionary, null), "不是键值对要报错");
            Assert.IsNotNull(CExcelCellJson.Check("atk=1;atk=2", CExcelFieldKind.Dictionary, null), "键重复要报错");
        }

        /// <summary>空单元格要有确定性的默认值（不能产出非法 JSON）。</summary>
        [Test]
        public void EmptyCells_ProduceValidDeterministicDefaults()
        {
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                if (CExcelTypeInfer.IsEnumKind(spec.Kind)) continue;
                Type scalar = CExcelJsonProbe.ResolveType(spec.CSharpType);

                string scalarJson = CExcelCellJson.Literal("", spec.Kind, null, out string scalarError);
                Assert.IsNull(scalarError, $"{spec.Suffix} 的空值不该报错");
                Assert.DoesNotThrow(() => CExcelJsonBackend.Deserialize(scalarJson, scalar),
                    $"{spec.Suffix} 的空值 JSON 非法：{scalarJson}");

                string arrayJson = CExcelCellJson.Literal("", spec.ArrayKind, null, out string arrayError);
                Assert.IsNull(arrayError, $"{spec.ArraySuffix} 的空值不该报错");
                Assert.AreEqual("[]", arrayJson, $"{spec.ArraySuffix} 空单元格应是空数组（不是 null、不是默认元素）");
                Assert.DoesNotThrow(() => CExcelJsonBackend.Deserialize(arrayJson, scalar.MakeArrayType()));
            }
        }

        /// <summary>
        /// **为什么必须换 Newtonsoft**：这些类型 JsonUtility 全都读不回来（实测 `{}`）。
        /// 这个测试把"换回去"变成一件会红的事。
        /// </summary>
        [Test]
        public void JsonUtility_CannotHandleTheTypesWePromise()
        {
            foreach (CExcelFieldKind kind in new[]
                     {
                         CExcelFieldKind.BigInt, CExcelFieldKind.Decimal, CExcelFieldKind.DateTime,
                         CExcelFieldKind.TimeSpan, CExcelFieldKind.Guid, CExcelFieldKind.Rect,
                         CExcelFieldKind.Dictionary,
                     })
            {
                Assert.IsTrue(CExcelTypeCatalog.NeedsNewtonsoft(kind),
                    $"{kind} 需要 Newtonsoft，映射表里要标 NeedsNewtonsoft = true（窗口据此提示用户）");
            }

            // Rect 是这一堆里最容易漏的：JsonUtility 只吐 {}（它的 x/y 是属性不是字段）
            Assert.AreEqual("{}", JsonUtility.ToJson(new Rect(0, 0, 100, 50)),
                "Rect 必须走 Newtonsoft —— JsonUtility 读不回它");

            // 反面：这些类型 JsonUtility **能**处理，不要误标成需要 Newtonsoft（文档要诚实）
            Assert.AreEqual("{\"x\":1.0,\"y\":2.0,\"z\":3.0}", JsonUtility.ToJson(new Vector3(1, 2, 3)));
            foreach (CExcelFieldKind kind in new[]
                     {
                         CExcelFieldKind.Vector2, CExcelFieldKind.Vector3, CExcelFieldKind.Vector4,
                         CExcelFieldKind.Quaternion, CExcelFieldKind.Color,
                     })
            {
                Assert.IsFalse(CExcelTypeCatalog.NeedsNewtonsoft(kind),
                    $"{kind} JsonUtility 本来就能读，不该标成需要 Newtonsoft");
            }

            Rect rect = Read<Rect>("0,0,100,50", CExcelFieldKind.Rect);
            Assert.AreEqual(100f, rect.width, 0.001f);
        }

        /// <summary>生成的 JSON 必须是**合法 JSON 文本**（用手写器，最容易在这里出错）。</summary>
        [Test]
        public void GeneratedJson_IsValidJsonText()
        {
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                if (CExcelTypeInfer.IsEnumKind(spec.Kind)) continue;
                string json = CExcelCellJson.Literal(spec.Example, spec.Kind, null, out _);
                Assert.IsTrue(CExcelJsonProbe.IsValidJson(json), $"{spec.Suffix} 的 JSON 文本非法：{json}");
            }
        }

        /// <summary>字符串转义必须完整（引号、反斜杠、换行、控制字符）。</summary>
        [Test]
        public void StringEscaping_CoversQuotesBackslashesAndControlChars()
        {
            const string nasty = "a\"b\\c\nd\te\u0001f中文";
            string json = CExcelCellJson.Literal(nasty, CExcelFieldKind.String, null, out _);
            Assert.IsTrue(CExcelJsonProbe.IsValidJson(json), "含控制字符的字符串也要产出合法 JSON");
            Assert.AreEqual(nasty, CExcelJsonBackend.Deserialize(json, typeof(string)));
        }

        private static T Read<T>(string cell, CExcelFieldKind kind)
        {
            string json = CExcelCellJson.Literal(cell, kind, null, out string error);
            Assert.IsNull(error, $"\"{cell}\" 生成 JSON 失败：{error}");
            return (T)CExcelJsonBackend.Deserialize(json, typeof(T));
        }
    }
}
