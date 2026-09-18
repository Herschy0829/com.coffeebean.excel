using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>
    /// 数据容器（.cbcfg）与生成端加密的测试：
    /// 编解码往返 / 密文不可读 / 生成器产出容器 / **Getter 与加密开关解耦**（flags 在容器头部，不在代码里）。
    /// </summary>
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
            string decoded = CExcelCrypto.Decode(cipher);
            StringAssert.Contains("中文测试", decoded);
            StringAssert.Contains("日本語テスト", decoded);
            StringAssert.Contains("🎮🔥", decoded);
            StringAssert.Contains("a\\\"b\\\\c\\nd\\t", decoded);
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
        public void Generate_MultiLanguageTable_ContainerDecodesCorrectly()
        {
            // 完整链路：含多语言数据的表 → 压缩 + 加密生成 → 按运行时的方式解容器
            string xlsx = CExcelTestFactory.CreateTempTable(new[]
            {
                CExcelTestFactory.Row("Id_i", 1, "Zh_s", "中文测试", "Ja_s", "日本語テスト", "Emoji_s", "🎮🔥"),
                CExcelTestFactory.Row("Id_i", 2, "Zh_s", "空值", "Ja_s", "", "Emoji_s", ""),
            }, "lang_test");
            try
            {
                CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut, "Config");
                options.ClassName = "LanguageTable";
                CExcelGenerateResult result = CExcelGenerator.Generate(xlsx, options);
                Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

                string dataPath = CExcelTestFactory.DataPath(options, "LanguageTable", "LanguageTable");
                byte[] container = File.ReadAllBytes(dataPath);
                Assert.IsFalse(ContainsAscii(container, "中文测试"), "容器里不应出现明文中文（已加密）");

                string decoded = CExcelDataContainer.Decode(container, out string error);
                Assert.IsNotNull(decoded, error);
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
        public void Generate_EncryptedData_ProducesBinaryContainer()
        {
            CExcelGenerateOptions options = CExcelTestFactory.TempPackageOptions(_tmpOut);
            options.ClassName = "CryptoTable";
            CExcelGenerateResult result = CExcelGenerator.Generate(_tmpXlsx, options);
            Assert.IsTrue(result.Success, string.Join("\n", result.Issues));

            string dataPath = CExcelTestFactory.DataPath(options, "CryptoTable", "CryptoTable");
            Assert.IsTrue(File.Exists(dataPath), "数据应是 " + CExcelDataContainer.Extension + " 容器: " + dataPath);

            byte[] container = File.ReadAllBytes(dataPath);
            Assert.IsFalse(ContainsAscii(container, "新手礼包"), "加密后的数据不应包含明文数据");
            Assert.IsFalse(ContainsAscii(container, "data"), "加密后的数据不应包含明文结构标记");

            string decoded = CExcelDataContainer.Decode(container, out string error);
            Assert.IsNotNull(decoded, error);
            StringAssert.Contains("新手礼包", decoded);
            StringAssert.Contains("\"Id\"", decoded);
        }

        [Test]
        public void Getter_IsIndependentOfCompressAndEncryptFlags()
        {
            // 关键不变量：压缩/加密开关只影响**数据文件头部 flags**，不该影响生成的代码。
            // 这样运行时的 Getter 永远走同一条解码路径（容器自带 flags），也就不存在
            // "生成端关了加密、运行时却按加密读" 这种错配。
            CExcelGenerateOptions on = CExcelTestFactory.TempPackageOptions(_tmpOut);
            on.ClassName = "FlagTable";
            CExcelGenerateResult r1 = CExcelGenerator.Generate(_tmpXlsx, on);
            Assert.IsTrue(r1.Success, string.Join("\n", r1.Issues));
            string getterOn = File.ReadAllText(CExcelTestFactory.CodePath(on, "FlagTable", "FlagTableGetter.cs"));

            string other = Path.Combine(Path.GetTempPath(), "coffeebean_crypto_off_" + Guid.NewGuid().ToString("N"));
            try
            {
                CExcelGenerateOptions off = CExcelTestFactory.TempPackageOptions(other);
                off.ClassName = "FlagTable";
                off.CompressData = false;
                off.EncryptData = false;
                CExcelGenerateResult r2 = CExcelGenerator.Generate(_tmpXlsx, off);
                Assert.IsTrue(r2.Success, string.Join("\n", r2.Issues));
                string getterOff = File.ReadAllText(CExcelTestFactory.CodePath(off, "FlagTable", "FlagTableGetter.cs"));

                Assert.AreEqual(getterOn, getterOff, "Getter 不应随压缩/加密开关变化");

                // 不压不加密时：容器头 16 字节之后就是明文 JSON（便于排查）
                byte[] raw = File.ReadAllBytes(CExcelTestFactory.DataPath(off, "FlagTable", "FlagTable"));
                Assert.AreEqual(0, raw[5], "两个 flag 都应关掉");
                var payload = new byte[raw.Length - CExcelDataContainer.HeaderSize];
                Array.Copy(raw, CExcelDataContainer.HeaderSize, payload, 0, payload.Length);
                StringAssert.Contains("新手礼包", Encoding.UTF8.GetString(payload), "关闭压缩+加密时应能直接看到明文（便于排查）");

                // 打开两个开关后：flags 两位都应置上。
                // 体积对比不放在这里 —— 小表的 Deflate 可能反而变大，压缩效果由
                // CExcelDataContainerTests.Encode_CompressionActuallyShrinks 用大样本覆盖。
                byte[] compressed = File.ReadAllBytes(CExcelTestFactory.DataPath(on, "FlagTable", "FlagTable"));
                Assert.AreEqual(
                    CExcelDataContainer.FlagCompressed | CExcelDataContainer.FlagEncrypted,
                    compressed[5],
                    "应记录为已压缩 + 已加密（flags 在容器头部，所以 Getter 无需区分）");
            }
            finally
            {
                CExcelTestFactory.DeleteTempFile(other);
                if (Directory.Exists(other)) Directory.Delete(other, true);
            }
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
