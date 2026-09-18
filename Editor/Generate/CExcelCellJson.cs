using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using UnityEngine;

namespace CoffeeBean
{
    /// <summary>
    /// 单元格文本 → JSON 字面量（**校验与生成共用同一份实现**，所以"校验通过的"一定"生成得出来"）。
    ///
    /// 为什么不是 <c>JsonUtility</c>/<c>JsonConvert.SerializeObject</c>：
    /// · JsonUtility 读不回 BigInteger / decimal / DateTime / Guid / Dictionary / Rect（实测全是 <c>{}</c>）；
    /// · Newtonsoft **序列化** Unity 结构会炸（Vector3.normalized 自引用死循环）——
    ///   所以 JSON 文本由本类手写，Newtonsoft 只负责**反**序列化。
    ///
    /// 空单元格 → 该类型的默认值（数值 0、字符 \0、时间/标识用确定性零值、结构全 0）。
    /// </summary>
    public static class CExcelCellJson
    {
        /// <summary>
        /// 单元格文本 → JSON 字面量。<paramref name="error"/> 非空 = 值非法（此时返回 <c>0</c>，调用方应把它当错误处理）。
        /// </summary>
        public static string Literal(string raw, CExcelFieldKind kind, CExcelEnumDef enumDef, out string error)
        {
            error = null;
            string text = (raw ?? string.Empty).Trim();
            if (text.Length == 0) return DefaultLiteral(kind);

            // 数组必须先判：EnumArray 也满足 IsEnumKind（它的元素是枚举）
            if (CExcelTypeInfer.IsArray(kind))
            {
                CExcelFieldKind element = CExcelTypeInfer.ElementKind(kind);
                List<string> parts = SplitArray(text, element);
                var sb = new StringBuilder();
                sb.Append('[');
                for (int i = 0; i < parts.Count; i++)
                {
                    string elementLiteral = Literal(parts[i], element, enumDef, out string elementError);
                    if (elementError != null)
                    {
                        error = $"数组第 {i + 1} 项：{elementError}";
                        return "[]";
                    }
                    if (i > 0) sb.Append(',');
                    sb.Append(elementLiteral);
                }
                sb.Append(']');
                return sb.ToString();
            }

            if (CExcelTypeInfer.IsEnumKind(kind))
            {
                if (enumDef == null)
                {
                    error = "缺少枚举定义（内部错误）";
                    return "0";
                }
                bool ok = kind == CExcelFieldKind.Flags
                    ? enumDef.TryResolveFlags(text, out long flags, out error)
                    : enumDef.TryResolve(text, out flags, out error);
                return ok ? flags.ToString(CultureInfo.InvariantCulture) : "0";
            }

            return ScalarLiteral(text, kind, out error);
        }

        /// <summary>只校验（不产出 JSON）：返回 null = 合法。</summary>
        public static string Check(string raw, CExcelFieldKind kind, CExcelEnumDef enumDef)
        {
            Literal(raw, kind, enumDef, out string error);
            return error;
        }

        /// <summary>
        /// 数组拆分。**按元素类型选分隔符**（这是踩过的坑：向量/颜色/矩形/字典的元素内部也用逗号或分号，
        /// 所以它们不能拿 `,`/`;` 当数组分隔符）：
        /// · 字典（_kva）→ `|`（元素内部的键值对用 `,` `;`）
        /// · 向量 / 四元数 / 颜色 / 矩形 → `;`（**不认 `,`** —— `1,2` 是一个二维向量）
        /// · 其余（含枚举数组、[Flags] 数组）→ `;` 或 `,`；[Flags] 数组的每个元素内部再用 `|` 组合
        /// </summary>
        public static List<string> SplitArray(string text, CExcelFieldKind elementKind)
        {
            switch (elementKind)
            {
                case CExcelFieldKind.Dictionary:
                    return CExcelTypeInfer.SplitGroupValue(text);
                case CExcelFieldKind.Vector2:
                case CExcelFieldKind.Vector3:
                case CExcelFieldKind.Vector4:
                case CExcelFieldKind.Quaternion:
                case CExcelFieldKind.Color:
                case CExcelFieldKind.Rect:
                    return CExcelTypeInfer.SplitOnAny(text, new[] { ';', '；', '|', '｜' });
                default:
                    return CExcelTypeInfer.SplitArrayValue(text);
            }
        }

        /// <summary>空单元格的默认 JSON 字面量。</summary>
        public static string DefaultLiteral(CExcelFieldKind kind)
        {
            if (CExcelTypeInfer.IsArray(kind)) return "[]";

            switch (kind)
            {
                case CExcelFieldKind.String: return "\"\"";
                case CExcelFieldKind.Char: return "\"\\u0000\"";
                case CExcelFieldKind.Bool: return "false";
                case CExcelFieldKind.DateTime: return "\"0001-01-01T00:00:00\"";
                case CExcelFieldKind.TimeSpan: return "\"00:00:00\"";
                case CExcelFieldKind.Guid: return "\"00000000-0000-0000-0000-000000000000\"";
                case CExcelFieldKind.Vector2: return "{\"x\":0,\"y\":0}";
                case CExcelFieldKind.Vector3: return "{\"x\":0,\"y\":0,\"z\":0}";
                case CExcelFieldKind.Vector4: return "{\"x\":0,\"y\":0,\"z\":0,\"w\":0}";
                case CExcelFieldKind.Quaternion: return "{\"x\":0,\"y\":0,\"z\":0,\"w\":1}";
                case CExcelFieldKind.Color: return "{\"r\":0,\"g\":0,\"b\":0,\"a\":0}";
                case CExcelFieldKind.Rect: return "{\"x\":0,\"y\":0,\"width\":0,\"height\":0}";
                case CExcelFieldKind.Dictionary: return "{}";
                default: return "0";
            }
        }

        // ========== 标量 ==========

        private static string ScalarLiteral(string text, CExcelFieldKind kind, out string error)
        {
            error = null;
            switch (kind)
            {
                case CExcelFieldKind.Int:
                case CExcelFieldKind.Long:
                case CExcelFieldKind.Byte:
                case CExcelFieldKind.SByte:
                case CExcelFieldKind.Short:
                case CExcelFieldKind.UShort:
                case CExcelFieldKind.UInt:
                case CExcelFieldKind.ULong:
                    return IntegerLiteral(text, kind, out error);

                case CExcelFieldKind.BigInt:
                    if (text.IndexOf('.') >= 0 || text.IndexOf('E') >= 0 || text.IndexOf('e') >= 0)
                    {
                        error = $"\"{text}\" 不是整数（Excel 把超 15 位的数字记成了科学计数法/小数 —— 请把这一列设成**文本格式**重新填大数）";
                        return "0";
                    }
                    if (!BigInteger.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger big))
                    {
                        error = $"\"{text}\" 不是合法整数";
                        return "0";
                    }
                    return big.ToString(CultureInfo.InvariantCulture);

                case CExcelFieldKind.Float:
                    if (!float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float f) || float.IsNaN(f) || float.IsInfinity(f))
                    {
                        error = $"\"{text}\" 不是合法的单精度数";
                        return "0";
                    }
                    return f.ToString("R", CultureInfo.InvariantCulture);

                case CExcelFieldKind.Double:
                    if (!double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || double.IsInfinity(d))
                    {
                        error = $"\"{text}\" 不是合法的双精度数";
                        return "0";
                    }
                    return d.ToString("R", CultureInfo.InvariantCulture);

                case CExcelFieldKind.Decimal:
                    if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal m))
                    {
                        error = $"\"{text}\" 不是合法的小数";
                        return "0";
                    }
                    return m.ToString(CultureInfo.InvariantCulture);

                case CExcelFieldKind.Bool:
                    if (!CExcelTypeInfer.IsBoolLiteral(text))
                    {
                        error = $"\"{text}\" 不是布尔值（只认 true/false/1/0）";
                        return "false";
                    }
                    return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1" ? "true" : "false";

                case CExcelFieldKind.Char:
                    if (text.Length != 1)
                    {
                        error = $"\"{text}\" 不是单个字符（长度 {text.Length}）";
                        return "\"\\u0000\"";
                    }
                    return Quote(text);

                case CExcelFieldKind.DateTime:
                    if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                    {
                        error = $"\"{text}\" 不是合法日期时间";
                        return "\"0001-01-01T00:00:00\"";
                    }
                    return Quote(dt.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture));

                case CExcelFieldKind.TimeSpan:
                    if (!TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out TimeSpan span))
                    {
                        error = $"\"{text}\" 不是合法时长（写 hh:mm:ss）";
                        return "\"00:00:00\"";
                    }
                    return Quote(span.ToString("c", CultureInfo.InvariantCulture));

                case CExcelFieldKind.Guid:
                    if (!Guid.TryParse(text, out Guid guid))
                    {
                        error = $"\"{text}\" 不是合法 Guid";
                        return "\"00000000-0000-0000-0000-000000000000\"";
                    }
                    return Quote(guid.ToString("D"));

                case CExcelFieldKind.Vector2:
                case CExcelFieldKind.Vector3:
                case CExcelFieldKind.Vector4:
                case CExcelFieldKind.Quaternion:
                case CExcelFieldKind.Rect:
                    return StructLiteral(text, kind, out error);

                case CExcelFieldKind.Color:
                    return ColorLiteral(text, out error);

                case CExcelFieldKind.Dictionary:
                    return DictionaryLiteral(text, out error);

                default:
                    return Quote(text);
            }
        }

        // ========== 整数 ==========

        private static string IntegerLiteral(string text, CExcelFieldKind kind, out string error)
        {
            error = null;

            if (kind == CExcelFieldKind.ULong)
            {
                if (!ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong ul))
                {
                    error = $"\"{text}\" 不是合法的 ulong（0~18446744073709551615）";
                    return "0";
                }
                return ul.ToString(CultureInfo.InvariantCulture);
            }

            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            {
                error = $"\"{text}\" 不是整数";
                return "0";
            }

            long min, max;
            switch (kind)
            {
                case CExcelFieldKind.Byte: min = byte.MinValue; max = byte.MaxValue; break;
                case CExcelFieldKind.SByte: min = sbyte.MinValue; max = sbyte.MaxValue; break;
                case CExcelFieldKind.Short: min = short.MinValue; max = short.MaxValue; break;
                case CExcelFieldKind.UShort: min = ushort.MinValue; max = ushort.MaxValue; break;
                case CExcelFieldKind.UInt: min = uint.MinValue; max = uint.MaxValue; break;
                case CExcelFieldKind.Int: min = int.MinValue; max = int.MaxValue; break;
                default: min = long.MinValue; max = long.MaxValue; break;
            }

            if (value < min || value > max)
            {
                error = $"\"{text}\" 超出 {kind} 的范围（{min}~{max}）";
                return "0";
            }
            return value.ToString(CultureInfo.InvariantCulture);
        }

        // ========== Unity 结构 ==========

        private static string StructLiteral(string text, CExcelFieldKind kind, out string error)
        {
            error = null;

            if (kind == CExcelFieldKind.Quaternion)
            {
                // 4 个数 = (x,y,z,w)；3 个数 = 欧拉角（角度）
                if (!TryParseNumbers(text, 3, 4, out double[] q, out error)) return DefaultLiteral(kind);
                if (q.Length == 4) return NumberObject("x", q[0], "y", q[1], "z", q[2], "w", q[3]);
                // 写全名：System.Numerics 里也有 Quaternion（.NET Core 3.0+），直接写 Quaternion 会歧义
                UnityEngine.Quaternion euler = UnityEngine.Quaternion.Euler((float)q[0], (float)q[1], (float)q[2]);
                return NumberObject("x", euler.x, "y", euler.y, "z", euler.z, "w", euler.w);
            }

            int count = kind == CExcelFieldKind.Vector2 ? 2 : kind == CExcelFieldKind.Vector3 ? 3 : 4;
            if (!TryParseNumbers(text, count, count, out double[] values, out error)) return DefaultLiteral(kind);

            switch (kind)
            {
                case CExcelFieldKind.Vector2:
                    return NumberObject("x", values[0], "y", values[1]);
                case CExcelFieldKind.Vector3:
                    return NumberObject("x", values[0], "y", values[1], "z", values[2]);
                case CExcelFieldKind.Vector4:
                    return NumberObject("x", values[0], "y", values[1], "z", values[2], "w", values[3]);
                default: // Rect
                    return NumberObject("x", values[0], "y", values[1], "width", values[2], "height", values[3]);
            }
        }

        private static string ColorLiteral(string text, out string error)
        {
            error = null;
            if (text.StartsWith("#", StringComparison.Ordinal))
            {
                string hex = text.Substring(1);
                if (!TryParseHexColor(hex, out float r, out float g, out float b, out float a))
                {
                    error = $"\"{text}\" 不是合法颜色（#RRGGBB / #RRGGBBAA / #RGB / #RGBA）";
                    return DefaultLiteral(CExcelFieldKind.Color);
                }
                return NumberObject("r", r, "g", g, "b", b, "a", a);
            }

            if (!TryParseNumbers(text, 3, 4, out double[] parts, out error))
                return DefaultLiteral(CExcelFieldKind.Color);

            // 规则：全是整数且有一个 > 1 → 按 0~255；否则按 0~1
            bool allIntegral = true;
            bool anyAboveOne = false;
            foreach (double v in parts)
            {
                if (v != Math.Floor(v)) allIntegral = false;
                if (v > 1) anyAboveOne = true;
            }
            double scale = allIntegral && anyAboveOne ? 1.0 / 255.0 : 1.0;
            float alpha = parts.Length == 4 ? (float)(parts[3] * scale) : 1f;

            return NumberObject("r", parts[0] * scale, "g", parts[1] * scale, "b", parts[2] * scale, "a", alpha);
        }

        private static bool TryParseHexColor(string hex, out float r, out float g, out float b, out float a)
        {
            r = g = b = 0f;
            a = 1f;
            foreach (char c in hex)
                if (!Uri.IsHexDigit(c)) return false;

            switch (hex.Length)
            {
                case 3:
                case 4:
                    r = HexByte(new string(hex[0], 2)) / 255f;
                    g = HexByte(new string(hex[1], 2)) / 255f;
                    b = HexByte(new string(hex[2], 2)) / 255f;
                    if (hex.Length == 4) a = HexByte(new string(hex[3], 2)) / 255f;
                    return true;
                case 6:
                case 8:
                    r = HexByte(hex.Substring(0, 2)) / 255f;
                    g = HexByte(hex.Substring(2, 2)) / 255f;
                    b = HexByte(hex.Substring(4, 2)) / 255f;
                    if (hex.Length == 8) a = HexByte(hex.Substring(6, 2)) / 255f;
                    return true;
                default:
                    return false;
            }
        }

        private static float HexByte(string twoHex)
            => byte.Parse(twoHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        // ========== 字典 ==========

        private static string DictionaryLiteral(string text, out string error)
        {
            error = null;
            var pairs = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (string token in CExcelTypeInfer.SplitOnAny(text, new[] { ';', '；', ',', '，' }))
            {
                int split = IndexOfAny(token, new[] { '=', ':' });
                if (split <= 0)
                {
                    error = $"\"{token}\" 不是键值对（写 键=值，如 atk=10）";
                    return "{}";
                }

                string key = token.Substring(0, split).Trim();
                string value = token.Substring(split + 1).Trim();
                if (key.Length == 0)
                {
                    error = $"\"{token}\" 的键是空的";
                    return "{}";
                }
                if (!seen.Add(key))
                {
                    error = $"键 \"{key}\" 重复了（同一组里每个键只能出现一次）";
                    return "{}";
                }
                pairs.Add(new KeyValuePair<string, string>(key, value));
            }

            var sb = new StringBuilder();
            sb.Append('{');
            for (int i = 0; i < pairs.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Quote(pairs[i].Key)).Append(':').Append(Quote(pairs[i].Value));
            }
            sb.Append('}');
            return sb.ToString();
        }

        // ========== 公共小工具 ==========

        /// <summary>逗号/分号/空格分隔的数字列表，个数必须在 [min,max] 内。</summary>
        private static bool TryParseNumbers(string text, int min, int max, out double[] values, out string error)
        {
            values = null;
            error = null;
            List<string> parts = CExcelTypeInfer.SplitOnAny(text, new[] { ',', '，', ';', '；', '|', '｜', ' ' });
            if (parts.Count < min || parts.Count > max)
            {
                error = min == max
                    ? $"需要 {min} 个数字（逗号分隔），实际给了 {parts.Count} 个"
                    : $"需要 {min}~{max} 个数字（逗号分隔），实际给了 {parts.Count} 个";
                return false;
            }

            values = new double[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])
                    || double.IsNaN(values[i]) || double.IsInfinity(values[i]))
                {
                    error = $"第 {i + 1} 个数 \"{parts[i]}\" 不是合法数字";
                    return false;
                }
            }
            return true;
        }

        private static int IndexOfAny(string text, char[] chars)
        {
            for (int i = 0; i < text.Length; i++)
                foreach (char c in chars)
                    if (text[i] == c) return i;
            return -1;
        }

        private static string NumberObject(params object[] nameValuePairs)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            for (int i = 0; i + 1 < nameValuePairs.Length; i += 2)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(nameValuePairs[i]).Append("\":");
                double v = Convert.ToDouble(nameValuePairs[i + 1], CultureInfo.InvariantCulture);
                sb.Append(((float)v).ToString("R", CultureInfo.InvariantCulture));
            }
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>JSON 字符串转义（中文不转义，保留 UTF-8）。</summary>
        public static string Quote(string s)
        {
            var sb = new StringBuilder((s?.Length ?? 0) + 8);
            sb.Append('"');
            if (s != null)
            {
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        default:
                            if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
