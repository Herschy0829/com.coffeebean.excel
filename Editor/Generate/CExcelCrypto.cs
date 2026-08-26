using System;
using System.Text;

namespace CoffeeBean.Excel
{
    /// <summary>
    /// 配置 JSON 的简单混淆加密（对齐 Idle 项目的 GetSimpleEncyptString 做法）。
    ///
    /// **安全边界**：这是"混淆级"保护——客户端资源可被解包工具提取、key 硬编码在生成的
    /// Getter 里可被反编译拿到，**不能防专业逆向**；目的是让打包产物里的配置不是明文
    /// （防止普通玩家/工具直接读取数值表、价格、奖励等）。
    /// 真正的安全需要服务器下发配置 / AssetBundle 加密 / 代码混淆。
    ///
    /// 算法：固定种子生成 key 字节流，与明文按位异或（含位置偏移）。
    /// </summary>
    public static class CExcelCrypto
    {
        /// <summary>加密密钥种子（生成端与 Getter 模板共用；改动会使旧产物无法解密，勿改）。</summary>
        public const string KeySeed = "CoffeeBean.Excel.Config.v1";

        /// <summary>编码：明文 UTF-8 → 密文字节（XOR + 位置偏移）。</summary>
        public static byte[] Encode(string plainText)
        {
            byte[] data = Encoding.UTF8.GetBytes(plainText ?? string.Empty);
            return Xor(data);
        }

        /// <summary>解码：密文字节 → 明文 UTF-8 字符串。</summary>
        public static string Decode(byte[] cipher)
        {
            if (cipher == null || cipher.Length == 0) return string.Empty;
            return Encoding.UTF8.GetString(Xor(cipher));
        }

        private static byte[] Xor(byte[] data)
        {
            byte[] key = GenerateKey(data.Length);
            var result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
                result[i] = (byte)(data[i] ^ key[i] ^ (byte)(i & 0x7F));
            return result;
        }

        private static byte[] GenerateKey(int length)
        {
            var key = new byte[length];
            uint state = 2166136261; // FNV-1a 起点
            for (int i = 0; i < length; i++)
            {
                // 由种子散列出伪随机字节流（确定性，生成端与运行时一致）
                state ^= KeySeed[i % KeySeed.Length];
                state *= 16777619;
                key[i] = (byte)(state >> 24);
            }
            return key;
        }
    }
}
