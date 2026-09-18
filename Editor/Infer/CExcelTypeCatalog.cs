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
    /// · 测试逐项核对（后缀往返、示例真实可解析、后缀无歧义、JSON 真能被 Newtonsoft 读回）。
    ///
    /// **每个标量 kind 恰好一条**（数组不单独占条目：数组后缀 = 标量后缀 + a，
    /// 数组类型 = 标量类型 + []，由 <see cref="ArrayKind"/>/<see cref="ArrayCSharpType"/> 给出）。
    /// </summary>
    public sealed class CExcelTypeSpec
    {
        public CExcelFieldKind Kind;

        /// <summary>对应的数组类型（由布局推导：标量下标 + <see cref="CExcelTypeInfer.ScalarCount"/>）。</summary>
        public CExcelFieldKind ArrayKind;

        /// <summary>标量后缀（如 <c>_i</c>）。</summary>
        public string Suffix;

        /// <summary>数组后缀（如 <c>_ia</c>，约定 = 标量后缀 + a）。</summary>
        public string ArraySuffix;

        /// <summary>生成的 C# 类型名（如 <c>int</c>）；枚举族是人读的说明（真正类型名来自表/枚举定义）。</summary>
        public string CSharpType;

        /// <summary>Excel 单元格里怎么写（标量示例）。</summary>
        public string Example;

        /// <summary>Excel 单元格里怎么写（数组示例）。</summary>
        public string ArrayExample;

        /// <summary>补充说明（边界/坑）。</summary>
        public string Note;

        /// <summary>字段名示例：列名 → 生成的字段名。</summary>
        public string ColumnExample;

        /// <summary>分组（窗口按组分块显示）。</summary>
        public string Group;

        /// <summary>是否只有 Newtonsoft 后端才能读回（JsonUtility 不支持）。</summary>
        public bool NeedsNewtonsoft;

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
    ///
    /// 表里每行显式声明 <see cref="SpecRow.Kind"/>；静态构造会核对"每个标量 kind 恰好一条"，
    /// 漏写/写重会在第一次访问时立刻抛（而不是安静地少一个类型）。
    /// </summary>
    public static class CExcelTypeCatalog
    {
        /// <summary>一行 = 一种标量类型（数组由其推导）。</summary>
        private sealed class SpecRow
        {
            public CExcelFieldKind Kind;
            public string Suffix;
            public string Type;
            public string Example;
            public string ArrayExample;
            public string Note;
            public string Column;
            public string Group;
            public bool NeedsNewtonsoft;
        }

        private static readonly SpecRow[] Rows =
        {
            // ---------- 整数 ----------
            Row(CExcelFieldKind.Int, "_i", "int", "整数", "123", "1;2;3", "Level_i → Level",
                "整数。超出 int32 范围请改用 _l（不会自动升位）。"),
            Row(CExcelFieldKind.Long, "_l", "long", "整数", "9999999999", "1;2;3", "Exp_l → Exp",
                "64 位整数。超过 long 请用 _b（BigInteger）。"),
            Row(CExcelFieldKind.BigInt, "_b", "System.Numerics.BigInteger", "整数", "12345678901234567890", "1;2;3", "Gold_b → Gold",
                "任意精度大整数（放置游戏的货币就用它）。⚠ 单元格要设成**文本格式**：Excel 数字格式只保留 15 位有效数字，再多会被吃掉或变成科学计数法 —— 遇到这种数据生成时**直接报错**，不会静默变 0。", true),
            Row(CExcelFieldKind.Byte, "_by", "byte", "整数", "200", "1;2;3", "Quality_by → Quality",
                "无符号字节 0~255。超范围报错。"),
            Row(CExcelFieldKind.SByte, "_sb", "sbyte", "整数", "-5", "1;2;3", "Offset_sb → Offset",
                "有符号字节 -128~127。"),
            Row(CExcelFieldKind.Short, "_sh", "short", "整数", "-30000", "1;2;3", "Delta_sh → Delta",
                "16 位有符号 -32768~32767。"),
            Row(CExcelFieldKind.UShort, "_us", "ushort", "整数", "60000", "1;2;3", "MaxHp_us → MaxHp",
                "16 位无符号 0~65535。"),
            Row(CExcelFieldKind.UInt, "_u", "uint", "整数", "4000000000", "1;2;3", "Uid_u → Uid",
                "32 位无符号 0~4294967295。"),
            Row(CExcelFieldKind.ULong, "_ul", "ulong", "整数", "18446744073709551615", "1;2;3", "Hash_ul → Hash",
                "64 位无符号。比 long 大，但不如 _b 任意精度。"),

            // ---------- 小数 ----------
            Row(CExcelFieldKind.Float, "_f", "float", "小数", "1.5", "0.5;1.25", "Rate_f → Rate",
                "单精度浮点。金额/累计值别用它，用 _dec。"),
            Row(CExcelFieldKind.Double, "_d", "double", "小数", "3.14159", "0.1;0.2", "Weight_d → Weight",
                "双精度浮点。无后缀的浮点列也会落到 double（无后缀推断没有 float 档）。"),
            Row(CExcelFieldKind.Decimal, "_dec", "decimal", "小数", "1.23", "0.1;0.2", "Price_dec → Price",
                "高精度十进制（金额、要求小数点后精确的数值）。比 float/double 精确。", true),

            // ---------- 布尔 ----------
            Row(CExcelFieldKind.Bool, "_bool", "bool", "布尔", "true", "true;false", "Enabled_bool → Enabled",
                "布尔。true/false/1/0 都认（不分大小写）。⚠ 注意 `_b` 是 BigInteger，不是 bool（旧版才是 bool）。"),

            // ---------- 文本 ----------
            Row(CExcelFieldKind.String, "_s", "string", "文本", "任意文本", "A;B;C", "Name_s → Name",
                "字符串。空单元格 → 空串（不是 null）。"),
            Row(CExcelFieldKind.Char, "_c", "char", "文本", "A", "A;B", "Grade_c → Grade",
                "单个字符。写了多个字符会报错。"),

            // ---------- 时间 / 标识 ----------
            Row(CExcelFieldKind.DateTime, "_time", "System.DateTime", "时间 / 标识", "2026-09-18 10:30:00", "2026-09-18;2026-09-19", "Start_time → Start",
                "日期时间。Excel 日期格式单元格、或文本 \"2026-09-18 10:30:00\" 都认（也认 2026/9/18）。JSON 里存 ISO 文本。", true),
            Row(CExcelFieldKind.TimeSpan, "_span", "System.TimeSpan", "时间 / 标识", "01:30:00", "00:10:00;01:00:00", "Cooldown_span → Cooldown",
                "时长。写成 hh:mm:ss（也认 \"1.02:03:04\" 这种 天.时:分:秒）。JSON 里存文本。", true),
            Row(CExcelFieldKind.Guid, "_guid", "System.Guid", "时间 / 标识", "6f9619ff-8b86-d011-b42d-00c04fc964ff", "6f9619ff-8b86-d011-b42d-00c04fc964ff", "Key_guid → Key",
                "Guid。JSON 里存 36 位文本。", true),

            // ---------- 枚举 ----------
            Row(CExcelFieldKind.Enum, "_e", "枚举（表内生成）", "枚举", "green", "green;idle", "State_e → State（类型 = 表名 + State）",
                "枚举。不带值从 0 起自动编号（按首次出现顺序）—— 示例就写 `green`；要指定值就在后面加下划线写数字（`green_3` → Green = 3，之后出现的自动编号取\"已用最大值 + 1\"）。" +
                "会**生成枚举类型**（名字 = 表名 + 字段名，如 BuildingState）。同一枚举里不允许两个成员同值；同名被赋两个值也报错；空单元格 → 0。" +
                " JSON 里存数字。也可写成 `State_e:已编译的枚举名` —— 那就**引用**已有枚举（不生成），成员名要与已编译枚举里的名字完全一致，生成时按实际枚举校验（类型/成员找不到直接报错）。"),
            Row(CExcelFieldKind.Flags, "_flags", "[Flags] 枚举（表内生成）", "枚举", "Fire|Ice", "Fire|Ice;Poison", "Tags_flags → Tags",
                "位标记枚举，多个成员用 | 连接（也认 , 和 ;）。成员编号规则同 _e。JSON 里存按位或后的数字。" +
                " 也可写 `Tags_flags:已编译的枚举名` 引用已有 [Flags] 枚举。"),

            // ---------- 结构（Unity） ----------
            Row(CExcelFieldKind.Vector2, "_v2", "UnityEngine.Vector2", "结构（Unity）", "1,2", "1,2;3,4", "Offset_v2 → Offset",
                "二维向量。逗号分隔 2 个数。"),
            Row(CExcelFieldKind.Vector3, "_v3", "UnityEngine.Vector3", "结构（Unity）", "1,2,3", "1,2,3;4,5,6", "Pos_v3 → Pos",
                "三维向量。逗号分隔 3 个数。"),
            Row(CExcelFieldKind.Vector4, "_v4", "UnityEngine.Vector4", "结构（Unity）", "1,2,3,4", "1,2,3,4;5,6,7,8", "Tint_v4 → Tint",
                "四维向量。逗号分隔 4 个数。"),
            Row(CExcelFieldKind.Quaternion, "_quat", "UnityEngine.Quaternion", "结构（Unity）", "0,0,0,1", "0,0,0,1;0,0.707,0,0.707", "Rot_quat → Rot",
                "旋转。**4 个数** = (x,y,z,w)；**3 个数** = 欧拉角（角度，Unity 的 ZXY 顺序）。"),
            Row(CExcelFieldKind.Color, "_color", "UnityEngine.Color", "结构（Unity）", "#FF8800", "#FF0000;#00FF00", "Tint_color → Tint",
                "颜色。三种写法都认：`#RRGGBB` / `#RRGGBBAA`（也认 `#RGB` / `#RGBA` 短写）、`255,136,0`（3~4 个**整数**按 0~255）、" +
                "`1,0.5,0,1`（带小数按 0~1）。不给 alpha 时不透明。"),
            Row(CExcelFieldKind.Rect, "_rect", "UnityEngine.Rect", "结构（Unity）", "0,0,100,50", "0,0,10,10;5,5,20,20", "Area_rect → Area",
                "矩形：x,y,width,height（4 个数）。", true),

            // ---------- 复合 ----------
            Row(CExcelFieldKind.Dictionary, "_kv", "Dictionary<string,string>", "复合", "atk=10;hp=20", "atk=1;hp=2|atk=3", "Attrs_kv → Attrs",
                "键值对字典。对之间用 ; 或 ,，键与值之间用 = 或 :。**数组**（_kva）用 | 分隔多组。同一组里键重复报错。", true),
        };

        private static SpecRow Row(CExcelFieldKind kind, string suffix, string type, string group,
            string example, string arrayExample, string column, string note, bool needsNewtonsoft = false)
            => new SpecRow
            {
                Kind = kind, Suffix = suffix, Type = type, Group = group,
                Example = example, ArrayExample = arrayExample, Column = column,
                Note = note, NeedsNewtonsoft = needsNewtonsoft,
            };

        private static readonly CExcelTypeSpec[] Specs = BuildSpecs();

        private static CExcelTypeSpec[] BuildSpecs()
        {
            int scalarCount = CExcelTypeInfer.ScalarCount;
            var byKind = new CExcelTypeSpec[scalarCount];
            foreach (SpecRow row in Rows)
            {
                int index = (int)row.Kind;
                if (index < 0 || index >= scalarCount)
                    throw new InvalidOperationException($"[CoffeeBean.Excel] 类型表里的 {row.Kind} 不是标量类型（布局必须是标量块 + 数组块）");
                if (byKind[index] != null)
                    throw new InvalidOperationException($"[CoffeeBean.Excel] 类型表里 {row.Kind} 有重复条目（每个标量类型必须恰好一条）");

                byKind[index] = new CExcelTypeSpec
                {
                    Kind = row.Kind,
                    ArrayKind = (CExcelFieldKind)(index + scalarCount),
                    Suffix = row.Suffix,
                    ArraySuffix = row.Suffix + "a",
                    CSharpType = row.Type,
                    Example = row.Example,
                    ArrayExample = row.ArrayExample,
                    Note = row.Note,
                    ColumnExample = row.Column,
                    Group = row.Group,
                    NeedsNewtonsoft = row.NeedsNewtonsoft,
                };
            }

            var specs = new List<CExcelTypeSpec>(scalarCount);
            for (int i = 0; i < scalarCount; i++)
            {
                if (byKind[i] == null)
                    throw new InvalidOperationException($"[CoffeeBean.Excel] 类型表缺少标量类型 {(CExcelFieldKind)i}（每个标量类型都必须有一条）");
                specs.Add(byKind[i]);
            }
            return specs.ToArray();
        }

        /// <summary>规划中的类型（窗口里单独一区，标注为什么还不能用）。</summary>
        private static readonly CExcelPlannedType[] Planned =
        {
            new CExcelPlannedType
            {
                Suffix = "_obj:类型", CSharpType = "嵌套对象", Example = "{\"hp\":10,\"atk\":5}",
                Blocker = "待实现：单元格里直接写 JSON 片段再反序列化成自定义类。需要先定\"类型必须先编译好\"的解析与校验规则。",
            },
            new CExcelPlannedType
            {
                Suffix = "_iaa / _saa", CSharpType = "二维（锯齿）数组", Example = "1;2|3;4",
                Blocker = "待实现：需要二级分隔符（| 分组、; 组内）。目前请用 _sa 自己二次拆分。",
            },
        };

        /// <summary>全部类型（每个标量 kind 一条；数组由其推导）。</summary>
        public static IReadOnlyList<CExcelTypeSpec> All => Specs;

        /// <summary>规划中的类型。</summary>
        public static IReadOnlyList<CExcelPlannedType> PlannedTypes => Planned;

        /// <summary>
        /// 数组后缀那行显示的说明。**各类型的分隔符规则不一样**（踩过坑）：
        /// 向量/颜色/矩形的元素内部就是逗号，字典数组的元素内部是分号 —— 它们不能再用这些当数组分隔符。
        /// </summary>
        public static string ArrayNote(CExcelFieldKind kind)
        {
            switch (CExcelTypeInfer.IsArray(kind) ? CExcelTypeInfer.ElementKind(kind) : kind)
            {
                case CExcelFieldKind.Vector2:
                case CExcelFieldKind.Vector3:
                case CExcelFieldKind.Vector4:
                case CExcelFieldKind.Quaternion:
                case CExcelFieldKind.Color:
                case CExcelFieldKind.Rect:
                    return "数组：**只能用 ; 分隔**（元素内部是逗号分隔的分量，所以 `,` 不能当数组分隔符）；空单元格 → 空数组。";
                case CExcelFieldKind.Dictionary:
                    return "数组：用 `|` 分隔多组（组内键值对用 `;` 或 `,`）；空单元格 → 空数组。";
                case CExcelFieldKind.Flags:
                    return "数组：`;` 或 `,` 分隔元素，元素内部再用 `|` 组合位标记；空单元格 → 空数组。";
                default:
                    return "数组：分隔符 ; 或 ,（中文 ；， 也认）；空单元格 → 空数组。";
            }
        }

        /// <summary>按 kind 取说明（数组会回落到元素类型那条）；找不到返回 null。</summary>
        public static CExcelTypeSpec Find(CExcelFieldKind kind)
        {
            CExcelFieldKind scalar = CExcelTypeInfer.IsArray(kind) ? CExcelTypeInfer.ElementKind(kind) : kind;
            for (int i = 0; i < Specs.Length; i++)
            {
                if (Specs[i].Kind == scalar) return Specs[i];
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
                ? Find(kind)?.ArraySuffix.Length ?? 3
                : Find(kind)?.Suffix.Length ?? 2;

        /// <summary>某类型是否必须 Newtonsoft 后端（JsonUtility 读不回）。</summary>
        public static bool NeedsNewtonsoft(CExcelFieldKind kind)
            => Find(kind)?.NeedsNewtonsoft ?? false;
    }
}
