using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>
    /// 数据容器 <c>.cbcfg</c> 编解码测试。
    /// 容器是"生成端与运行时唯一有格式契约的地方"（运行时代码由 CExcelRuntimeTemplate 生成），
    /// 所以这里要把格式的每个字段、每种失败模式都钉死。
    /// </summary>
    public class CExcelDataContainerTests
    {
        private const string Json = "{\"data\":[{\"Id\":1,\"Name\":\"新手礼包\"},{\"Id\":2,\"Name\":\"月卡\"}]}";

        [Test]
        public void EncodeDecode_CompressedAndEncrypted_RoundTrip()
        {
            byte[] container = CExcelDataContainer.Encode(Json, compress: true, encrypt: true);

            Assert.AreEqual(CExcelDataContainer.Version, container[4], "版本字节");
            Assert.AreEqual(CExcelDataContainer.FlagCompressed | CExcelDataContainer.FlagEncrypted, container[5], "flags 两位都应置上");
            Assert.AreEqual(Encoding.UTF8.GetByteCount(Json), BitConverter.ToInt32(container, 8), "rawLength 应记录原始 UTF-8 字节数（不是字符数）");

            string decoded = CExcelDataContainer.Decode(container, out string error);
            Assert.IsNull(error, error);
            Assert.AreEqual(Json, decoded);
        }

        [Test]
        public void Encode_CompressionActuallyShrinks()
        {
            var sb = new StringBuilder("{\"data\":[");
            for (int i = 0; i < 200; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"Id\":").Append(i).Append(",\"Name\":\"重复内容重复内容重复内容\"}");
            }
            sb.Append("]}");
            string big = sb.ToString();

            byte[] compressed = CExcelDataContainer.Encode(big, compress: true, encrypt: false);
            byte[] raw = CExcelDataContainer.Encode(big, compress: false, encrypt: false);

            Assert.Less(compressed.Length, raw.Length / 2, "重复内容应被 Deflate 显著压缩");
            Assert.AreEqual(big, CExcelDataContainer.Decode(compressed, out string e1), e1);
            Assert.AreEqual(big, CExcelDataContainer.Decode(raw, out string e2), e2);
        }

        [Test]
        public void Decode_RejectsWrongMagic()
        {
            byte[] container = CExcelDataContainer.Encode(Json, true, true);
            container[0] = (byte)'X';

            Assert.IsNull(CExcelDataContainer.Decode(container, out string error));
            StringAssert.Contains("魔术字", error);
        }

        [Test]
        public void Decode_RejectsUnsupportedVersion()
        {
            byte[] container = CExcelDataContainer.Encode(Json, true, true);
            container[4] = 99;

            Assert.IsNull(CExcelDataContainer.Decode(container, out string error));
            StringAssert.Contains("版本", error);
        }

        [Test]
        public void Decode_RejectsTruncatedPayload()
        {
            byte[] container = CExcelDataContainer.Encode(Json, true, true);
            var truncated = new byte[container.Length - 3];
            Array.Copy(container, truncated, truncated.Length);

            Assert.IsNull(CExcelDataContainer.Decode(truncated, out string error), "截断的数据必须被拒绝，不能解出半个表");
            Assert.IsNotNull(error);
        }

        [Test]
        public void Decode_DetectsCorruption_ByChecksum()
        {
            // 关掉压缩，这样只翻转一个明文字节：能精确验证"校验和兜住损坏"
            byte[] container = CExcelDataContainer.Encode(Json, compress: false, encrypt: false);
            container[CExcelDataContainer.HeaderSize + 5] ^= 0xFF;

            Assert.IsNull(CExcelDataContainer.Decode(container, out string error));
            StringAssert.Contains("校验和", error);
        }

        [Test]
        public void Decode_RejectsTooShortInput()
        {
            Assert.IsNull(CExcelDataContainer.Decode(new byte[4], out string e1)); Assert.IsNotNull(e1);
            Assert.IsNull(CExcelDataContainer.Decode(null, out string e2)); Assert.IsNotNull(e2);
        }

        [Test]
        public void Encode_NoCompressNoEncrypt_KeepsPayloadAsPlainJson()
        {
            byte[] container = CExcelDataContainer.Encode(Json, compress: false, encrypt: false);

            Assert.AreEqual(0, container[5], "两个 flags 都应关掉");
            Assert.AreEqual(CExcelDataContainer.HeaderSize + Encoding.UTF8.GetByteCount(Json), container.Length);
            Assert.AreEqual(Json, CExcelDataContainer.Decode(container, out string error), error);
        }

        [Test]
        public void Encode_IsDeterministic()
        {
            CollectionAssert.AreEqual(
                CExcelDataContainer.Encode(Json, true, true),
                CExcelDataContainer.Encode(Json, true, true),
                "同一输入必须产出同一字节（否则无法做产物比对/缓存）");
        }
    }
}
