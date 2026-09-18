using System;
using System.Collections.Generic;

namespace CoffeeBean
{
    /// <summary>
    /// 一种列类型后缀的完整说明（**映射表的单一数据源**）。
    ///
    /// 为什么要有它：后缀表原来散落在 <c>CExcelTypeInfer</c> 的常量与 if 链里，
    /// "文档 / 窗口 / 实际解析"三处各写一遍必然漂移。现在统一放这里：
    /// · 解析（<see cref="CExcelTypeInfer.FromSuffix"/>）按它匹配；
    /// · 映射窗口（CExcelTypeMappingWindow）按它渲染；
    /// · 测试逐项核对（后缀往返、示例真实可解析、无歧义后缀）。
    /// </summary>
    public sealed class CExcelTypeSpec
    {
        public CExcelFieldKind Kind;

        /// <summary>对应的数组类型（显式写出，不靠 <c>Kind + 6</c> 这种隐式约定）。</summary>
        public CExcelFieldKind ArrayKind;

        /// <summary>标量后缀（如 <c>_i</c>）。</summary>
        public string Suffix;

        /// <summary>数组后缀（如 <c>_ia</c>）。</summary>
        public string ArraySuffix;

        /// <summary>生成的 C# 类型名（如 <c>int</c> / <c>int[]</c>）。</summary>
        public string CSharpType;

        /// <summary>Excel 单元格里怎么写（标量示例）。</summary>
        public string Example;

        /// <summary>Excel 单元格里怎么写（数组示例）。</summary>
        public string ArrayExample;

        /// <summary>补充说明（边界/坑）。</summary>
        public string Note;

        /// <summary>字段名示例：列名 → 生成的字段名。</summary>
        public string ColumnExample;

        public string ArrayCSharpType => CSharpType + "[]";
    }

    /// <summary>规划中（尚未实现）的类型，同样在映射窗口里列出，避免"以为能用"。</summary>
    public sealed class CExcelPlannedType
    {
        public string Suffix;
        public string CSharpType;
        public string Example;
        /// <summary>为什么现在还不能用。</summary>
        public string Blocker;
    }

    /// <summary>
    /// 列类型映射表：后缀 ↔ C# 类型 ↔ 单元格写法示例。**框架内唯一的类型表**。
    /// </summary>
    public static class CExcelTypeCatalog
    {
        private static readonly CExcelTypeSpec[] Specs =
        {
            new CExcelTypeSpec
            {
                Kind = CExcelFieldKind.Int, ArrayKind = CExcelFieldKind.IntArray, Suffix = "_i", ArraySuffix = "_ia", CSharpType = "int",
                Example = "123", ArrayExample = "1;2;3", ColumnExample = "Level_i → Level",
                Note = "整数。超出 int32 范围请改用 _l（不要指望自动升位）。",
            },
            new CExcelTypeSpec
            {
                Kind = CExcelFieldKind.Long, ArrayKind = CExcelFieldKind.LongArray, Suffix = "_l", ArraySuffix = "_la", CSharpType = "long",
                Example = "9999999999", ArrayExample = "1;2;3", ColumnExample = "Exp_l → Exp",
                Note = "64 位整数。再大（超过 long）需要 BigInteger，见规划中类型。",
            },
            new CExcelTypeSpec
            {
                Kind = CExcelFieldKind.Float, ArrayKind = CExcelFieldKind.FloatArray, Suffix = "_f", ArraySuffix = "_fa", CSharpType = "float",
                Example = "1.5", ArrayExample = "0.5;1.25", ColumnExample = "Rate_f → Rate",
                Note = "单精度浮点。金额/累计值建议用 _d 或 BigInteger。",
            },
            new CExcelTypeSpec
            {
                Kind = CExcelFieldKind.Double, ArrayKind = CExcelFieldKind.DoubleArray, Suffix = "_d", ArraySuffix = "_da", CSharpType = "double",
                Example = "3.14159", ArrayExample = "0.1;0.2", ColumnExample = "Weight_d → Weight",
                Note = "双精度浮点。",
            },
            new CExcelTypeSpec
            {
                Kind = CExcelFieldKind.Bool, ArrayKind = CExcelFieldKind.BoolArray, Suffix = "_b", ArraySuffix = "_ba", CSharpType = "bool",
                Example = "true", ArrayExample = "true;false", ColumnExample = "Enabled_b → Enabled",
                Note = "布尔。true/false/1/0 都认（不分大小写）。⚠ 无后缀时纯 1/0 的列会被推成 int（推断里 int 优先），想当 bool 必须写 _b。",
            },
            new CExcelTypeSpec
            {
                Kind = CExcelFieldKind.String, ArrayKind = CExcelFieldKind.StringArray, Suffix = "_s", ArraySuffix = "_sa", CSharpType = "string",
                Example = "任意文本", ArrayExample = "A;B;C", ColumnExample = "Name_s → Name",
                Note = "字符串。空单元格 → 空串（不是 null）。",
            },
        };

        /// <summary>规划中的类型（窗口里单独一区，标注为什么还不能用）。</summary>
        private static readonly CExcelPlannedType[] Planned =
        {
            new CExcelPlannedType
            {
                Suffix = "_e / _ea", CSharpType = "枚举 / 枚举[]", Example = "green  或  green_3（3 = 显式枚举值）",
                Blocker = "待实现。语法已定：不带值从 0 起自动编号；带 _值 用显式值；生成时校验\"不能有同一枚举值\"。",
            },
            new CExcelPlannedType
            {
                Suffix = "_b（改）", CSharpType = "System.Numerics.BigInteger", Example = "12345678901234567890",
                Blocker = "待实现，且会与现有 _b(bool) 冲突：需先用 Newtonsoft 后端（JsonUtility 读不了 BigInteger），bool 改到 _bool。",
            },
            new CExcelPlannedType
            {
                Suffix = "_bool / _boola", CSharpType = "bool / bool[]", Example = "true;false",
                Blocker = "待实现：配合 _b 让位给 BigInteger 后，bool 的新后缀。",
            },
            new CExcelPlannedType
            {
                Suffix = "_dec", CSharpType = "decimal", Example = "1.23",
                Blocker = "需 Newtonsoft 后端（JsonUtility 不支持 decimal）。",
            },
            new CExcelPlannedType
            {
                Suffix = "_time / _span", CSharpType = "DateTime / TimeSpan", Example = "2026-09-18 10:30:00 / 01:30:00",
                Blocker = "需 Newtonsoft 后端。",
            },
            new CExcelPlannedType
            {
                Suffix = "_guid", CSharpType = "System.Guid", Example = "8f3b…（36 位）",
                Blocker = "需 Newtonsoft 后端。",
            },
            new CExcelPlannedType
            {
                Suffix = "_kv", CSharpType = "Dictionary<string,string>", Example = "atk=10;hp=20",
                Blocker = "需 Newtonsoft 后端（JsonUtility 不支持字典）。",
            },
            new CExcelPlannedType
            {
                Suffix = "_v3 / _color / _rect", CSharpType = "Vector3 / Color / Rect", Example = "1,2,3  /  #FF8800  /  0,0,100,50",
                Blocker = "待实现：这两个 JsonUtility 也支持，加后缀 + 解析规则即可。",
            },
        };

        /// <summary>全部已支持的类型（每个 kind 一条）。</summary>
        public static IReadOnlyList<CExcelTypeSpec> All => Specs;

        /// <summary>规划中的类型。</summary>
        public static IReadOnlyList<CExcelPlannedType> PlannedTypes => Planned;

        /// <summary>按 kind 取说明；找不到返回 null。</summary>
        public static CExcelTypeSpec Find(CExcelFieldKind kind)
        {
            for (int i = 0; i < Specs.Length; i++)
            {
                if (Specs[i].Kind == kind) return Specs[i];
            }
            return null;
        }

        /// <summary>
        /// 按列名匹配后缀 → 类型；无后缀返回 null。
        ///
        /// 匹配顺序：**先数组后缀再标量后缀**（纪律：数组后缀都更长更具体）。
        /// 另外有测试保证"任何后缀都不是另一个后缀的结尾"，所以这里的顺序只是防御性写法。
        /// </summary>
        public static CExcelFieldKind? BySuffix(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return null;
            string lower = columnName.ToLowerInvariant();

            for (int i = 0; i < Specs.Length; i++)
            {
                if (lower.EndsWith(Specs[i].ArraySuffix, StringComparison.Ordinal)) return Specs[i].ArrayKind;
            }
            for (int i = 0; i < Specs.Length; i++)
            {
                if (lower.EndsWith(Specs[i].Suffix, StringComparison.Ordinal)) return Specs[i].Kind;
            }
            return null;
        }

        /// <summary>后缀串长度（字段名去后缀用；从表里取，不再硬编码 2/3）。</summary>
        public static int SuffixLength(CExcelFieldKind kind)
            => CExcelTypeInfer.IsArray(kind)
                ? Find(CExcelTypeInfer.ElementKind(kind))?.ArraySuffix.Length ?? 3
                : Find(kind)?.Suffix.Length ?? 2;
    }
}
