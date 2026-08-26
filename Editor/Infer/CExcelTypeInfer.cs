using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CoffeeBean
{
    /// <summary>字段类型（列名后缀声明 + 无后缀推断）。</summary>
    public enum CExcelFieldKind
    {
        Int,
        Long,
        Float,
        Double,
        Bool,
        String,
        IntArray,
        LongArray,
        FloatArray,
        DoubleArray,
        BoolArray,
        StringArray,
    }

    /// <summary>
    /// 列类型推断：
    ///
    /// 1. **列名后缀显式声明**（对齐 purchase 表与项目约定）：
    ///    _i=int _l=long _f=float _d=double _b=bool _s=string；
    ///    数组加 a：_ia=int[] _sa=string[] ...；无后缀 → 按值推断
    /// 2. **无后缀兜底推断**：全整数 → int；超 int 范围 → long；含小数 → double；
    ///    全布尔字面量（true/false/1/0）→ bool；否则 string
    /// 3. 数组分隔符：;（含中文 ；）与 ,；空值 → 空数组
    /// </summary>
    public static class CExcelTypeInfer
    {
        private const string SuffixInt = "_i";
        private const string SuffixLong = "_l";
        private const string SuffixFloat = "_f";
        private const string SuffixDouble = "_d";
        private const string SuffixBool = "_b";
        private const string SuffixString = "_s";

        /// <summary>是否带类型后缀（_i/_l/_f/_d/_b/_s 或数组变体 _ia/_la/...）。</summary>
        public static bool IsSuffixed(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return false;
            return FromSuffix(columnName) != null;
        }

        /// <summary>按列名后缀解析类型；无后缀返回 null。</summary>
        public static CExcelFieldKind? FromSuffix(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return null;
            string lower = columnName.ToLowerInvariant();

            if (lower.EndsWith(SuffixInt, StringComparison.Ordinal)) return CExcelFieldKind.Int;
            if (lower.EndsWith(SuffixLong, StringComparison.Ordinal)) return CExcelFieldKind.Long;
            if (lower.EndsWith(SuffixFloat, StringComparison.Ordinal)) return CExcelFieldKind.Float;
            if (lower.EndsWith(SuffixDouble, StringComparison.Ordinal)) return CExcelFieldKind.Double;
            if (lower.EndsWith(SuffixBool, StringComparison.Ordinal)) return CExcelFieldKind.Bool;
            if (lower.EndsWith(SuffixString, StringComparison.Ordinal)) return CExcelFieldKind.String;

            if (lower.EndsWith("_ia", StringComparison.Ordinal)) return CExcelFieldKind.IntArray;
            if (lower.EndsWith("_la", StringComparison.Ordinal)) return CExcelFieldKind.LongArray;
            if (lower.EndsWith("_fa", StringComparison.Ordinal)) return CExcelFieldKind.FloatArray;
            if (lower.EndsWith("_da", StringComparison.Ordinal)) return CExcelFieldKind.DoubleArray;
            if (lower.EndsWith("_ba", StringComparison.Ordinal)) return CExcelFieldKind.BoolArray;
            if (lower.EndsWith("_sa", StringComparison.Ordinal)) return CExcelFieldKind.StringArray;

            return null;
        }

        /// <summary>
        /// 推断列类型：后缀优先；无后缀按该列全部非空值推断；空列 → String。
        /// </summary>
        public static CExcelFieldKind Infer(string columnName, IEnumerable<object> values)
        {
            CExcelFieldKind? bySuffix = FromSuffix(columnName);
            if (bySuffix.HasValue) return bySuffix.Value;

            bool sawValue = false;
            bool allInt = true;
            bool allLong = true;
            bool allBool = true;
            bool allDouble = true;

            foreach (object raw in values)
            {
                if (raw == null) continue;
                string text = CExcelValue.ToText(raw).Trim();
                if (text.Length == 0) continue;
                sawValue = true;

                bool isLong = TryParseLong(text, out long longValue);
                bool isDouble = TryParseDouble(text, out _);
                bool isBool = IsBoolLiteral(text);

                if (!isLong) allLong = false;
                if (!isBool) allBool = false;
                if (!isDouble) allDouble = false;
                // int 是 long 的子集：还需在 int32 范围内
                if (!isLong || !IsInt32Range(longValue)) allInt = false;
            }

            if (!sawValue) return CExcelFieldKind.String;

            if (allInt) return CExcelFieldKind.Int;
            if (allLong) return CExcelFieldKind.Long;
            if (allBool) return CExcelFieldKind.Bool;
            if (allDouble) return CExcelFieldKind.Double;
            return CExcelFieldKind.String;
        }

        /// <summary>是否数组类型。</summary>
        public static bool IsArray(CExcelFieldKind kind)
            => kind >= CExcelFieldKind.IntArray;

        /// <summary>数组类型的元素类型。</summary>
        public static CExcelFieldKind ElementKind(CExcelFieldKind arrayKind)
            => (CExcelFieldKind)((int)arrayKind - (int)CExcelFieldKind.IntArray);

        /// <summary>对应 C# 类型名（int/long/float/double/bool/string，数组加 []）。</summary>
        public static string CSharpType(CExcelFieldKind kind)
        {
            bool isArray = IsArray(kind);
            string baseType = isArray ? CSharpType(ElementKind(kind)) : null;
            switch (kind)
            {
                case CExcelFieldKind.Int:
                case CExcelFieldKind.IntArray: baseType = "int"; break;
                case CExcelFieldKind.Long:
                case CExcelFieldKind.LongArray: baseType = "long"; break;
                case CExcelFieldKind.Float:
                case CExcelFieldKind.FloatArray: baseType = "float"; break;
                case CExcelFieldKind.Double:
                case CExcelFieldKind.DoubleArray: baseType = "double"; break;
                case CExcelFieldKind.Bool:
                case CExcelFieldKind.BoolArray: baseType = "bool"; break;
                case CExcelFieldKind.String:
                case CExcelFieldKind.StringArray: baseType = "string"; break;
            }
            return isArray ? baseType + "[]" : baseType;
        }

        /// <summary>规范列名 → 字段名（去类型后缀，下划线转 PascalCase，首字母大写）。</summary>
        public static string ToFieldName(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return columnName;
            // 去类型后缀（_i/_ia/...）
            CExcelFieldKind? kind = FromSuffix(columnName);
            string name = kind.HasValue
                ? columnName.Substring(0, columnName.Length - SuffixLength(kind.Value))
                : columnName;

            // 下划线/中划线 → 驼峰，首字母大写
            var sb = new StringBuilder(name.Length);
            bool upperNext = true;
            foreach (char c in name)
            {
                if (c == '_' || c == '-' || c == ' ')
                {
                    upperNext = true;
                    continue;
                }
                sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                upperNext = false;
            }
            string result = sb.ToString();
            if (result.Length == 0) return columnName;
            return char.ToUpperInvariant(result[0]) + (result.Length > 1 ? result.Substring(1) : string.Empty);
        }

        private static int SuffixLength(CExcelFieldKind kind)
            => IsArray(kind) ? 3 : 2;

        /// <summary>解析数组值（分隔符 ; 或 ,，支持中文 ；）。返回元素文本列表。</summary>
        public static List<string> SplitArrayValue(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            string[] parts = text.Split(new[] { ';', '；', ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0) result.Add(trimmed);
            }
            return result;
        }

        private static bool TryParseLong(string text, out long value)
            => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static bool TryParseDouble(string text, out double value)
            => double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

        private static bool IsInt32Range(long value) => value >= int.MinValue && value <= int.MaxValue;

        private static bool IsBoolLiteral(string text)
            => string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)
               || text == "1" || text == "0";
    }
}
