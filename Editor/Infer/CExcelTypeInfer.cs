using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CoffeeBean
{
    /// <summary>
    /// 字段类型（列名后缀声明 + 无后缀推断）。
    ///
    /// **布局约定（有测试锁住）**：先列全部标量（0..<see cref="CExcelTypeInfer.ScalarCount"/>-1），
    /// 再按**完全相同的顺序**列对应数组；因此：
    /// · <c>IsArray(kind) = kind &gt;= IntArray</c>
    /// · <c>ElementKind(arrayKind) = arrayKind - ScalarCount</c>
    /// 加新类型时必须"标量 + 数组成对加在各自块的同一位置"，越界会被
    /// <c>CExcelTypeCatalogTests.ArrayKindMatchesInferConvention</c> 逮住。
    /// </summary>
    public enum CExcelFieldKind
    {
        // ================= 标量 =================
        Int,
        Long,
        Float,
        Double,
        Bool,
        String,
        BigInt,
        Decimal,
        DateTime,
        TimeSpan,
        Guid,
        Enum,
        Flags,
        Byte,
        SByte,
        Short,
        UShort,
        UInt,
        ULong,
        Char,
        Vector2,
        Vector3,
        Vector4,
        Quaternion,
        Color,
        Rect,
        Dictionary,

        // ================= 数组（顺序必须与上面逐一对应）=================
        IntArray,
        LongArray,
        FloatArray,
        DoubleArray,
        BoolArray,
        StringArray,
        BigIntArray,
        DecimalArray,
        DateTimeArray,
        TimeSpanArray,
        GuidArray,
        EnumArray,
        FlagsArray,
        ByteArray,
        SByteArray,
        ShortArray,
        UShortArray,
        UIntArray,
        ULongArray,
        CharArray,
        Vector2Array,
        Vector3Array,
        Vector4Array,
        QuaternionArray,
        ColorArray,
        RectArray,
        DictionaryArray,
    }

    /// <summary>
    /// 列类型：
    ///
    /// 1. **列名后缀显式声明**：见 <see cref="CExcelTypeCatalog"/> ——
    ///    后缀表、C# 类型、单元格写法示例**只有那一份数据源**，本类只负责按它匹配；
    ///    数组后缀 = 标量后缀 + a（_ia=int[] _sa=string[] ...）；无后缀 → 按值推断
    /// 2. **引用型枚举**：`State_e:MyEnum`（冒号后是已编译的枚举类型名，不生成枚举）
    /// 3. **无后缀兜底推断**：全整数 → int；超 int 范围 → long；含小数 → double；
    ///    全布尔字面量（true/false/1/0）→ bool；否则 string
    /// 4. 数组分隔符：;（含中文 ；）与 ,；空值 → 空数组
    /// </summary>
    public static class CExcelTypeInfer
    {
        /// <summary>引用型枚举的类型名分隔符：<c>State_e:MyEnum</c>。</summary>
        public const char EnumTypeSeparator = ':';

        /// <summary>标量个数（= 数组块起点）。新增类型时这里自动跟随枚举布局，不用手改。</summary>
        public static int ScalarCount => (int)CExcelFieldKind.IntArray;

        /// <summary>是否带类型后缀（见 <see cref="CExcelTypeCatalog"/>）。</summary>
        public static bool IsSuffixed(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return false;
            return FromSuffix(columnName) != null;
        }

        /// <summary>按列名后缀解析类型；无后缀返回 null。</summary>
        public static CExcelFieldKind? FromSuffix(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return null;
            return CExcelTypeCatalog.BySuffix(SuffixHead(columnName));
        }

        /// <summary>
        /// 去掉 <c>:类型名</c>尾巴后的部分（只有 `_e` / `_ea` 才认这个尾巴，别的列名里的冒号原样保留）。
        /// </summary>
        public static string SuffixHead(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return columnName;
            int colon = columnName.LastIndexOf(EnumTypeSeparator);
            if (colon <= 0 || colon == columnName.Length - 1) return columnName;
            string head = columnName.Substring(0, colon);
            return EndsWithEnumSuffix(head) ? head : columnName;
        }

        /// <summary>引用型枚举的类型名（<c>State_e:MyEnum</c> → <c>MyEnum</c>）；不是引用型返回 null。</summary>
        public static string EnumTypeName(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return null;
            int colon = columnName.LastIndexOf(EnumTypeSeparator);
            if (colon <= 0 || colon == columnName.Length - 1) return null;
            string head = columnName.Substring(0, colon);
            if (!EndsWithEnumSuffix(head)) return null;
            string type = columnName.Substring(colon + 1).Trim();
            return type.Length > 0 ? type : null;
        }

        private static bool EndsWithEnumSuffix(string head)
        {
            foreach (CExcelFieldKind kind in new[] { CExcelFieldKind.Enum, CExcelFieldKind.EnumArray, CExcelFieldKind.Flags, CExcelFieldKind.FlagsArray })
            {
                CExcelTypeSpec spec = CExcelTypeCatalog.Find(kind);
                if (spec == null) continue;
                string suffix = IsArray(kind) ? spec.ArraySuffix : spec.Suffix;
                if (head.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
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
            bool allIntegralText = true;

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
                // "看起来是整数"（没有小数点/指数）：超 long 的整数串不能掉进 double（会丢精度）
                if (!IsIntegerText(text)) allIntegralText = false;
            }

            if (!sawValue) return CExcelFieldKind.String;

            if (allInt) return CExcelFieldKind.Int;
            if (allLong) return CExcelFieldKind.Long;
            if (allBool) return CExcelFieldKind.Bool;
            // 只有"真的带小数/指数"才当 double；整数但超出 long（如 30 位数字串）保持 string，
            // 否则会静默变成丢精度的 double —— 想要大整数请显式写 _b（BigInteger）
            if (allDouble && !allIntegralText) return CExcelFieldKind.Double;
            return CExcelFieldKind.String;
        }

        /// <summary>文本是否"看起来是整数"（没有小数点、没有指数符号）。</summary>
        public static bool IsIntegerText(string text)
            => !string.IsNullOrEmpty(text)
               && text.IndexOf('.') < 0
               && text.IndexOf('e') < 0
               && text.IndexOf('E') < 0;

        /// <summary>是否数组类型。</summary>
        public static bool IsArray(CExcelFieldKind kind)
            => kind >= CExcelFieldKind.IntArray;

        /// <summary>数组类型的元素类型。</summary>
        public static CExcelFieldKind ElementKind(CExcelFieldKind arrayKind)
            => (CExcelFieldKind)((int)arrayKind - ScalarCount);

        /// <summary>
        /// 能否当主键（自动选主键列时按此筛选）：整数族 / 字符串 / Guid 这类"天然唯一"的类型。
        /// 浮点、布尔、结构、字典不做键（浮点做字典键还有 NaN 坑）；**数组一律不做键**。
        /// </summary>
        public static bool IsKeyCandidate(CExcelFieldKind kind)
        {
            if (IsArray(kind)) return false;
            switch (kind)
            {
                case CExcelFieldKind.Int:
                case CExcelFieldKind.Long:
                case CExcelFieldKind.BigInt:
                case CExcelFieldKind.Byte:
                case CExcelFieldKind.SByte:
                case CExcelFieldKind.Short:
                case CExcelFieldKind.UShort:
                case CExcelFieldKind.UInt:
                case CExcelFieldKind.ULong:
                case CExcelFieldKind.String:
                case CExcelFieldKind.Char:
                case CExcelFieldKind.Guid:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>数组去一层；非数组原样。</summary>
        public static CExcelFieldKind ElementKindIfArray(CExcelFieldKind kind)
            => IsArray(kind) ? ElementKind(kind) : kind;

        /// <summary>
        /// 列的"声明类型"可读名字（枚举族的实际类型名来自表/枚举定义，这里给的是预览用的说明）。
        /// </summary>
        public static string DescribeType(string columnName, CExcelFieldKind kind)
        {
            if (IsEnumKind(kind))
            {
                string suffix = IsArray(kind) ? "[]" : string.Empty;
                string external = EnumTypeName(columnName);
                if (external != null) return external + suffix + "（引用已编译枚举）";
                return "枚举" + suffix + "（生成：表名 + " + ToFieldName(columnName) + "）";
            }
            return CSharpType(kind);
        }

        /// <summary>是否枚举族（<c>_e</c>/<c>_ea</c>/<c>_flags</c>/<c>_flagsa</c>：类型名来自表，不来自类型表）。</summary>
        public static bool IsEnumKind(CExcelFieldKind kind)
        {
            CExcelFieldKind element = IsArray(kind) ? ElementKind(kind) : kind;
            return element == CExcelFieldKind.Enum || element == CExcelFieldKind.Flags;
        }

        /// <summary>枚举族里的"元素类型"（数组去一层）。</summary>
        public static CExcelFieldKind EnumElementKind(CExcelFieldKind kind)
            => IsArray(kind) ? ElementKind(kind) : kind;

        /// <summary>
        /// 对应 C# 类型名（见 <see cref="CExcelTypeCatalog"/>；数组加 []）。
        /// **枚举族返回 null** —— 它没有固定类型名，实际类型名来自表/枚举定义（<c>CExcelTable</c> 的 <c>Enums</c>）。
        /// </summary>
        public static string CSharpType(CExcelFieldKind kind)
        {
            if (IsEnumKind(kind)) return null;
            if (IsArray(kind))
            {
                CExcelTypeSpec element = CExcelTypeCatalog.Find(ElementKind(kind));
                if (element != null) return element.CSharpType + "[]";
            }
            CExcelTypeSpec spec = CExcelTypeCatalog.Find(kind);
            return spec != null ? spec.CSharpType : kind.ToString();
        }

        /// <summary>规范列名 → 字段名（去类型后缀 / 去 <c>:类型名</c>，下划线转 PascalCase，首字母大写）。</summary>
        public static string ToFieldName(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return columnName;
            // 去 `:类型名`（引用型枚举）与类型后缀（_i/_ia/...）
            string head = SuffixHead(columnName);
            CExcelFieldKind? kind = CExcelTypeCatalog.BySuffix(head);
            string name = kind.HasValue
                ? head.Substring(0, head.Length - SuffixLength(kind.Value))
                : head;

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
            => CExcelTypeCatalog.SuffixLength(kind);

        /// <summary>
        /// 规范列名 → **Legacy 字段名**（去类型后缀 / 去 <c>:类型名</c>，其余**原样保留**，不做驼峰转换、不改大小写）。
        ///
        /// 为什么需要它：项目既有生成器（AyFarme 的 JsonGenerator）就是原样取名 ——
        /// <c>mode_i</c> → <c>mode</c>、<c>Des_s</c> → <c>Des</c>、<c>isLord_i</c> → <c>isLord</c>；
        /// 而本模块的 <see cref="ToFieldName"/> 生成 PascalCase（<c>Mode</c>/<c>IsLord</c>）。
        /// 业务代码里写的是 <c>data.mode</c> / <c>data.quality</c> 这种小写开头的字段名，
        /// 只有原样命名才能让生成产物**直接替换**既有代码（实测差了 8 个字段就编译不过）。
        /// </summary>
        public static string ToLegacyFieldName(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return columnName;
            string head = SuffixHead(columnName);
            CExcelFieldKind? kind = CExcelTypeCatalog.BySuffix(head);
            string name = kind.HasValue
                ? head.Substring(0, head.Length - SuffixLength(kind.Value))
                : head;
            return name.Length == 0 ? columnName : name;
        }

        /// <summary>解析数组值（分隔符 ; 或 ,，支持中文 ；）。返回元素文本列表。</summary>
        public static List<string> SplitArrayValue(string text)
            => SplitOn(text, new[] { ';', '；', ',', '，' });

        /// <summary>解析"多组"分隔（字典数组、二维预留）：| 或 ｜。</summary>
        public static List<string> SplitGroupValue(string text)
            => SplitOn(text, new[] { '|', '｜' });

        /// <summary>按给定分隔符拆（去空、trim）。</summary>
        public static List<string> SplitOnAny(string text, char[] separators)
            => SplitOn(text, separators);

        private static List<string> SplitOn(string text, char[] separators)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            string[] parts = text.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0) result.Add(trimmed);
            }
            return result;
        }

        internal static bool TryParseLong(string text, out long value)
            => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        internal static bool TryParseDouble(string text, out double value)
            => double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

        private static bool IsInt32Range(long value) => value >= int.MinValue && value <= int.MaxValue;

        /// <summary>布尔字面量：true/false/1/0（不分大小写）。</summary>
        public static bool IsBoolLiteral(string text)
            => string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)
               || text == "1" || text == "0";
    }
}
