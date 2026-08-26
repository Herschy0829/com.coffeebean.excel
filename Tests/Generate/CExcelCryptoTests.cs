using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>配置 JSON 加密测试：编解码往返 / 密文不可读 / 生成器加密输出 / Getter 含解密。</summary>
    public class CExcelCryptoTests
    {
        private string _tmpXlsx;
        private string _tmpOut;

        [SetUp]
        public void SetUp()
        {
            _tmpXlsx = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Name_s", "新手礼包", "Price_f", 6.5),
            }, "crypto_test");
            _tmpOut = Path.Combine(Path.GetTempPath(), "coffeebean_crypto_out_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            CExcelTestFactory.DeleteTempFile(_tmpXlsx);
            if (Directory.Exists(_tmpOut)) Directory.Delete(_tmpOut, true);
        }

        [Test]
        public void EncodeDecode_RoundTrip_WithChinese()
        {
            string plain = "{\"data\":[{\"Id\":1,\"Name\":\"新手礼包\"}]}";
            byte[] cipher = CExcelCrypto.Encode(plain);

            Assert.AreNotEqual(plain, Encoding.UTF8.GetString(cipher), "密文不应等于明文");
            StringAssert.DoesNotContain("新手礼包", Encoding.UTF8.GetString(cipher), "密文中不应出现明文中文");

            string decoded = CExcelCrypto.Decode(cipher);
            Assert.AreEqual(plain, decoded, "解密应还原明文");
        }

        [Test]
        public void EncodeDecode_RoundTrip_MultiLanguage()
        {
            // 多语言表专项：中/日/emoji/转义字符——字节级 XOR 与编码无关，加密不应乱码
            string plain = "{\"data\":[{\"Id\":1,\"Zh\":\"中文测试\",\"Ja\":\"日本語テスト\",\"Emoji\":\"🎮🔥\",\"Esc\":\"a\\\"b\\\\c\\nd\\t\"}]}";
            byte[] cipher = CExcelCrypto.Encode(plain);

            Assert.AreEqual(plain, CExcelCrypto.Decode(cipher), "多语言文本加密往返应无损（无乱码）");
            // 逐项断言还原
            string decoded = CExcelCrypto.Decode(cipher);
            StringAssert.Contains("中文测试", decoded);
            StringAssert.Contains("日本語テスト", decoded);
            StringAssert.Contains("🎮🔥", decoded);
            StringAssert.Contains("a\\\"b\\\\c\\nd\\t", decoded);
        }

        [Test]
        public void Generate_MultiLanguageTable_EncryptedFileDecodesCorrectly()
        {
            // 完整链路：含多语言数据的表 → 加密生成 → 读文件字节 → 解密还原（模拟运行时 TextAsset.bytes）
            string xlsx = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Zh_s", "中文测试", "Ja_s", "日本語テスト", "Emoji_s", "🎮🔥"),
                CExcelTestFactory.Row("Id_i", 2, "Zh_s", "空值", "Ja_s", "", "Emoji_s", ""),
            }, "lang_test");
            try
            {
                var options = new CExcelGenerateOptions
                {
                    OutputFolder = _tmpOut,
                    ClassName = "LanguageTable",
                    JsonResourcesFolder = _tmpOut + "/Resources",
                    EncryptJson = true,
                };
                CExcelGenerateResult result = CExcelGenerator.Generate(xlsx, options);
                Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

                byte[] cipher = File.ReadAllBytes(Path.Combine(_tmpOut, "Resources", "LanguageTable.json"));
                Assert.IsFalse(ContainsAscii(cipher, "中文测试"), "密文文件不应含明文中文");

                string decoded = CExcelCrypto.Decode(cipher);
                StringAssert.Contains("中文测试", decoded);
                StringAssert.Contains("日本語テスト", decoded);
                StringAssert.Contains("🎮🔥", decoded);
            }
            finally
            {
                CExcelTestFactory.DeleteTempFile(xlsx);
            }
        }

        [Test]
        public void EncodeDecode_RoundTrip_Deterministic()
        {
            string plain = "{\"data\":[]}";
            byte[] c1 = CExcelCrypto.Encode(plain);
            byte[] c2 = CExcelCrypto.Encode(plain);

            CollectionAssert.AreEqual(c1, c2, "同一明文加密结果应确定（生成端与运行时一致）");
        }

        [Test]
        public void Generate_EncryptJson_ProducesCipherText()
        {
            var options = new CExcelGenerateOptions
            {
                OutputFolder = _tmpOut,
                ClassName = "CryptoTable",
                JsonResourcesFolder = _tmpOut + "/Resources",
                ResourcesPath = "Configs",
                EncryptJson = true,
            };
            CExcelGenerateResult result = CExcelGenerator.Generate(_tmpXlsx, options);
            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            string jsonPath = Path.Combine(_tmpOut, "Resources", "CryptoTable.json");
            byte[] cipher = File.ReadAllBytes(jsonPath);
            Assert.IsFalse(ContainsAscii(cipher, "新手礼包"), "加密后的 JSON 不应包含明文数据");
            Assert.IsFalse(ContainsAscii(cipher, "data"), "加密后的 JSON 不应包含明文结构标记");

            // 解密后应还原为合法 JSON（含中文数据）
            string decoded = CExcelCrypto.Decode(cipher);
            StringAssert.Contains("新手礼包", decoded);
            StringAssert.Contains("\"Id\"", decoded);

            // Getter 应内嵌解密逻辑
            string getter = File.ReadAllText(Path.Combine(_tmpOut, "CryptoTableGetter.cs"));
            StringAssert.Contains("Decode(asset.bytes)", getter);
            StringAssert.Contains("private static string Decode(byte[] data)", getter);
        }

        [Test]
        public void Generate_EncryptOff_WritesPlainText()
        {
            var options = new CExcelGenerateOptions
            {
                OutputFolder = _tmpOut,
                ClassName = "PlainTable",
                JsonResourcesFolder = _tmpOut + "/Resources",
                EncryptJson = false,
            };
            CExcelGenerator.Generate(_tmpXlsx, options);

            string json = File.ReadAllText(Path.Combine(_tmpOut, "Resources", "PlainTable.json"));
            StringAssert.Contains("新手礼包", json, "关闭加密时 JSON 应为明文");

            string getter = File.ReadAllText(Path.Combine(_tmpOut, "PlainTableGetter.cs"));
            StringAssert.Contains("asset.text", getter);
            Assert.IsFalse(getter.Contains("Decode(asset.bytes)"), "关闭加密时 Getter 不应含解密逻辑");
        }

        private static bool ContainsAscii(byte[] data, string text)
        {
            byte[] needle = Encoding.UTF8.GetBytes(text);
            for (int i = 0; i + needle.Length <= data.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (data[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return true;
            }
            return false;
        }
    }
}
