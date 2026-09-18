using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CoffeeBean
{
    /// <summary>
    /// 配置数据容器 <c>.cbcfg</c> 的编解码（生成端）。
    ///
    /// **布局**（小端）：
    /// <code>
    /// 0   magic       4B   'C','B','G','1'
    /// 4   version     1B   = 1
    /// 5   flags       1B   bit0 = 已压缩(Deflate)，bit1 = 已加密(XOR)
    /// 6   reserved    2B   = 0
    /// 8   rawLength   4B   原始 UTF-8 JSON 字节数
    /// 12  rawChecksum 4B   原始 JSON 的 FNV-1a 校验（用于识别截断/损坏）
    /// 16  payload          Deflate(JSON) → XOR
    /// </code>
    ///
    /// **顺序必须是"先压缩再加密"**：反过来（先 XOR）密文近似随机，Deflate 基本压不动。
    ///
    /// **为什么不用 .json 后缀**：扩展名决定第三方导入器会不会来抢（spine-unity 会对项目里
    /// 每个 <c>.json</c> 调 IsSpineData，密文不是 JSON 就逐条 Debug.LogError）。自定义扩展名
    /// 既避开这类碰撞，也表明"这不是文本文件"。
    ///
    /// **运行时解码实现在生成的 ConfigTableRuntime.cs 里**（生成代码不能引用本 Editor 程序集）——
    /// 改本文件必须同步改 <see cref="CExcelRuntimeTemplate"/>，否则产物解不开。
    /// </summary>
    public static class CExcelDataContainer
    {
        /// <summary>容器魔术字（同时充当"这是配置数据"的判据）。</summary>
        public static readonly byte[] MagicBytes = { (byte)'C', (byte)'B', (byte)'G', (byte)'1' };

        public const byte Version = 1;
        public const byte FlagCompressed = 1;
        public const byte FlagEncrypted = 2;
        public const int HeaderSize = 16;

        /// <summary>数据文件扩展名（含点）。</summary>
        public const string Extension = ".cbcfg";

        /// <summary>编码：JSON 文本 → 容器字节。</summary>
        public static byte[] Encode(string json, bool compress, bool encrypt)
        {
            byte[] raw = Encoding.UTF8.GetBytes(json ?? string.Empty);
            byte[] payload = compress ? Deflate(raw) : raw;
            if (encrypt) payload = CExcelCrypto.Xor(payload);

            byte flags = 0;
            if (compress) flags |= FlagCompressed;
            if (encrypt) flags |= FlagEncrypted;

            var result = new byte[HeaderSize + payload.Length];
            Buffer.BlockCopy(MagicBytes, 0, result, 0, 4);
            result[4] = Version;
            result[5] = flags;
            result[6] = 0;
            result[7] = 0;
            WriteUInt32(result, 8, (uint)raw.Length);
            WriteUInt32(result, 12, Checksum(raw));
            Buffer.BlockCopy(payload, 0, result, HeaderSize, payload.Length);
            return result;
        }

        /// <summary>解码：容器字节 → JSON 文本。失败时返回 null 并给出原因（不抛异常，便于回退到明文读法）。</summary>
        public static string Decode(byte[] container, out string error)
        {
            error = null;
            if (container == null || container.Length < HeaderSize)
            {
                error = "容器太短（" + (container == null ? 0 : container.Length) + " 字节）";
                return null;
            }

            for (int i = 0; i < 4; i++)
            {
                if (container[i] != MagicBytes[i])
                {
                    error = "魔术字不匹配（不是 .cbcfg 容器；明文 JSON / 旧格式？）";
                    return null;
                }
            }

            if (container[4] != Version)
            {
                error = "容器版本不支持: " + container[4] + "（本工具只认 " + Version + "）";
                return null;
            }

            byte flags = container[5];
            uint rawLength = ReadUInt32(container, 8);
            uint rawChecksum = ReadUInt32(container, 12);

            var payload = new byte[container.Length - HeaderSize];
            Buffer.BlockCopy(container, HeaderSize, payload, 0, payload.Length);

            if ((flags & FlagEncrypted) != 0) payload = CExcelCrypto.Xor(payload);
            if ((flags & FlagCompressed) != 0)
            {
                try
                {
                    payload = Inflate(payload, (int)rawLength);
                }
                catch (Exception e)
                {
                    error = "解压失败: " + e.Message;
                    return null;
                }
            }

            if (payload.Length != rawLength)
            {
                error = "长度不符: 头部声明 " + rawLength + "，实际 " + payload.Length;
                return null;
            }

            if (Checksum(payload) != rawChecksum)
            {
                error = "校验和不符（数据损坏或被篡改）";
                return null;
            }

            return Encoding.UTF8.GetString(payload);
        }

        /// <summary>Deflate 压缩（原始 deflate 流，无 zlib/gzip 头；与运行时 DeflateStream 对称）。</summary>
        public static byte[] Deflate(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
                {
                    deflate.Write(data, 0, data.Length);
                }
                return output.ToArray();
            }
        }

        /// <summary>Deflate 解压；容量按调用方给的预期长度预分配（不给也行，会自动扩容）。</summary>
        public static byte[] Inflate(byte[] data, int expectedLength = 0)
        {
            using (var input = new MemoryStream(data))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream(expectedLength > 0 ? expectedLength : 0))
            {
                deflate.CopyTo(output);
                return output.ToArray();
            }
        }

        /// <summary>FNV-1a 32 位校验（生成端与运行时必须一致）。</summary>
        public static uint Checksum(byte[] data)
        {
            uint hash = 2166136261;
            for (int i = 0; i < data.Length; i++)
            {
                hash ^= data[i];
                hash *= 16777619;
            }
            return hash;
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset]
                          | (buffer[offset + 1] << 8)
                          | (buffer[offset + 2] << 16)
                          | (buffer[offset + 3] << 24));
        }
    }
}
