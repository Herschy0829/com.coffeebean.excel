using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace CoffeeBean
{
    /// <summary>枚举的一个成员（一个不同的取值）。</summary>
    public sealed class CExcelEnumMember
    {
        /// <summary>单元格里原本写的名字（如 <c>green</c>）。</summary>
        public string RawName;

        /// <summary>生成的 C# 成员名（如 <c>Green</c>）；外部引用枚举时 = RawName（必须与编译好的名字一致）。</summary>
        public string Name;

        public long Value;

        /// <summary>首次出现的行号（1-based，报错用）。</summary>
        public int Row;

        /// <summary>首次出现的位置文案（章节表带 sheet 名）。</summary>
        public string Where;

        /// <summary>值是否来自 <c>name_值</c> 显式写法。</summary>
        public bool Explicit;
    }

    /// <summary>待建枚举的一个单元格取值。</summary>
    public struct CExcelEnumCell
    {
        public string Raw;
        public int Row;

        /// <summary>来自哪个 sheet（章节表的多 sheet 并集要靠它定位；单表为 null）。</summary>
        public string Sheet;

        public CExcelEnumCell(string raw, int row, string sheet = null)
        {
            Raw = raw;
            Row = row;
            Sheet = sheet;
        }

        /// <summary>报错定位文案：第 N 行 / [sheet] 第 N 行。</summary>
        public string Where => string.IsNullOrEmpty(Sheet) ? $"第 {Row} 行" : $"[{Sheet}] 第 {Row} 行";
    }

    /// <summary>
    /// 一列的枚举定义：<c>_e</c>/<c>_ea</c> 从表里**生成**枚举；<c>_e:类型名</c> 从已编译程序集里**引用**枚举。
    ///
    /// 两种模式在这里统一成"成员名 → 数值"，所以 JSON 生成、校验、窗口预览只有一条代码路径。
    /// </summary>
    public sealed class CExcelEnumDef
    {
        /// <summary>枚举类型名（生成用；引用模式 = 冒号后写的名字）。</summary>
        public string TypeName;

        /// <summary>来源列名。</summary>
        public string Column;

        /// <summary>true = 引用已编译枚举（不生成枚举类型）。</summary>
        public bool IsExternal;

        /// <summary>是否 [Flags]（单元格允许多个成员用 | 连接）。</summary>
        public bool IsFlags;

        /// <summary>该列是否数组（_ea / _flagsa）。</summary>
        public bool IsArray;

        /// <summary>引用模式解析到的实际类型（校验成员是否存在用）。</summary>
        public Type ExternalType;

        /// <summary>成员列表（生成模式 = 全部成员，按首次出现顺序；引用模式 = 本表用到的成员及其实数值）。</summary>
        public readonly List<CExcelEnumMember> Members = new List<CExcelEnumMember>();

        /// <summary>成员签名（跨表查重用：成员名与值都必须一致）。</summary>
        public string Signature
        {
            get
            {
                var sb = new StringBuilder();
                foreach (CExcelEnumMember member in Members)
                {
                    sb.Append(member.Name).Append('=').Append(member.Value.ToString(CultureInfo.InvariantCulture));
                    if (IsFlags) sb.Append("[flags]");
                    sb.Append(';');
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// 单个成员名 → 数值（<c>_e</c> 用）。
        /// 先按原样查（引用模式下成员名本身可能带下划线+数字），再按 <c>名字_值</c> 拆一次
        /// —— 生成模式的显式值写法要能解析，且显式值必须与定义里的一致。
        /// </summary>
        public bool TryResolve(string raw, out long value, out string error)
        {
            value = 0;
            error = null;
            string name = (raw ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                error = "空值";
                return false;
            }

            if (TryFind(name, out CExcelEnumMember exact))
            {
                value = exact.Value;
                return true;
            }

            if (CExcelEnumBuilder.SplitNameAndValue(name, out string bare, out long? explicitValue)
                && !string.Equals(bare, name, StringComparison.Ordinal)
                && TryFind(bare, out CExcelEnumMember member))
            {
                if (explicitValue.HasValue && explicitValue.Value != member.Value)
                {
                    error = $"成员 \"{bare}\" 定义的值是 {member.Value}，但单元格里写的是 {explicitValue.Value}（同一成员只能有一个值）";
                    return false;
                }
                value = member.Value;
                return true;
            }

            error = IsExternal
                ? $"枚举 {TypeName} 里没有成员 \"{name}\"（引用模式要求成员名与已编译枚举完全一致，区分大小写）"
                : $"枚举 {TypeName} 里没有成员 \"{name}\"";
            return false;
        }

        private bool TryFind(string name, out CExcelEnumMember found)
        {
            foreach (CExcelEnumMember member in Members)
            {
                if (Matches(member, name))
                {
                    found = member;
                    return true;
                }
            }
            found = null;
            return false;
        }

        /// <summary>[Flags] 成员组 → 按位或后的数值。</summary>
        public bool TryResolveFlags(string raw, out long value, out string error)
        {
            value = 0;
            error = null;
            List<string> tokens = CExcelTypeInfer.SplitOnAny(raw, new[] { '|', '｜', ',', '，', ';', '；' });
            if (tokens.Count == 0)
            {
                error = "空值";
                return false;
            }

            foreach (string token in tokens)
            {
                if (!TryResolve(token, out long single, out string singleError))
                {
                    error = singleError;
                    return false;
                }
                value |= single;
            }
            return true;
        }

        private bool Matches(CExcelEnumMember member, string name)
            => IsExternal
                ? string.Equals(member.Name, name, StringComparison.Ordinal)   // 引用模式：必须与编译好的名字完全一致
                : string.Equals(member.RawName, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 枚举定义构建：解析单元格取值（<c>green</c> / <c>green_3</c>）、自动编号、成员名规范化与全部校验。
    ///
    /// 校验清单（都产出 <see cref="CExcelIssue"/>，错误级会阻塞生成）：
    /// · 同一枚举里两个成员同值（"不能有同一枚举值"）
    /// · 同一个名字被赋了两个不同的值
    /// · 名字取不出合法 C# 标识符
    /// · 引用模式下枚举类型找不到 / 成员不存在
    /// · 枚举列一个取值都没有（C# 枚举不能为空）
    /// </summary>
    public static class CExcelEnumBuilder
    {
        private static CExcelIssue Error(int row, string column, string message)
            => new CExcelIssue { Level = CExcelIssueLevel.Error, Row = row, Column = column, Message = message };

        private static CExcelIssue Warning(int row, string column, string message)
            => new CExcelIssue { Level = CExcelIssueLevel.Warning, Row = row, Column = column, Message = message };

        /// <summary>
        /// 构建一列的枚举定义。
        /// </summary>
        /// <param name="columnName">列名（含后缀；引用模式含 <c>:类型名</c>）。</param>
        /// <param name="kind">枚举族里的元素类型（Enum 或 Flags）。</param>
        /// <param name="isArray">该列是否数组。</param>
        /// <param name="typeNamePrefix">生成模式的类型名前缀（表名/章节名前缀）。</param>
        /// <param name="cells">该列的单元格取值（按行序；章节表是各章节拼接后的并集）。</param>
        /// <param name="issues">问题输出。</param>
        public static CExcelEnumDef Build(string columnName, CExcelFieldKind kind, bool isArray,
            string typeNamePrefix, IReadOnlyList<CExcelEnumCell> cells, List<CExcelIssue> issues)
        {
            bool isFlags = kind == CExcelFieldKind.Flags;
            string externalTypeName = CExcelTypeInfer.EnumTypeName(columnName);
            var def = new CExcelEnumDef
            {
                Column = columnName,
                IsFlags = isFlags,
                IsArray = isArray,
                IsExternal = externalTypeName != null,
                TypeName = externalTypeName ?? (typeNamePrefix + CExcelTypeInfer.ToFieldName(columnName)),
            };

            if (externalTypeName != null)
            {
                BuildExternal(def, externalTypeName, cells, issues);
                return def;
            }

            BuildGenerated(def, cells, issues);
            return def;
        }

        // ========== 生成模式 ==========

        private static void BuildGenerated(CExcelEnumDef def, IReadOnlyList<CExcelEnumCell> cells, List<CExcelIssue> issues)
        {
            var byRaw = new Dictionary<string, CExcelEnumMember>(StringComparer.OrdinalIgnoreCase);
            long nextAuto = 0;

            foreach (CExcelEnumCell cell in cells)
            {
                string text = (cell.Raw ?? string.Empty).Trim();
                if (text.Length == 0) continue;

                foreach (string token in SplitCellTokens(text, def.IsFlags, def.IsArray))
                {
                    if (!SplitNameAndValue(token, out string rawName, out long? explicitValue)) continue;
                    if (rawName.Length == 0) continue;

                    if (byRaw.TryGetValue(rawName, out CExcelEnumMember existing))
                    {
                        if (explicitValue.HasValue && explicitValue.Value != existing.Value)
                        {
                            issues.Add(Error(cell.Row, def.Column,
                                $"枚举成员 \"{rawName}\" 被赋了两个不同的值：{existing.Where} 是 {existing.Value}，{cell.Where} 写的是 {explicitValue.Value}。同一成员只能有一个值。"));
                        }
                        continue;
                    }

                    string name = SanitizeMemberName(rawName);
                    if (name == null)
                    {
                        issues.Add(Error(cell.Row, def.Column,
                            $"枚举取值 \"{rawName}\" 取不出合法的 C# 成员名（请用字母/数字/下划线，且至少含一个字母）。"));
                        continue;
                    }
                    if (name != rawName)
                    {
                        issues.Add(Warning(cell.Row, def.Column,
                            $"枚举取值 \"{rawName}\" 会生成成员名 \"{name}\"（自动规范成 PascalCase；引用模式 _e:类型 则要求与已编译名字完全一致）。"));
                    }

                    long value;
                    if (explicitValue.HasValue)
                    {
                        value = explicitValue.Value;
                        if (nextAuto <= value) nextAuto = value + 1;   // 自动编号永远取"已用最大值 + 1"
                    }
                    else
                    {
                        value = nextAuto++;
                    }

                    var member = new CExcelEnumMember
                    {
                        RawName = rawName, Name = name, Value = value, Row = cell.Row, Where = cell.Where, Explicit = explicitValue.HasValue,
                    };
                    def.Members.Add(member);
                    byRaw[rawName] = member;
                }
            }

            if (def.Members.Count == 0)
            {
                issues.Add(Error(0, def.Column, "枚举列没有任何取值 —— 无法生成空的 C# 枚举。请至少给一行填上取值。"));
                return;
            }

            // 不能有同一枚举值
            var byValue = new Dictionary<long, CExcelEnumMember>();
            foreach (CExcelEnumMember member in def.Members)
            {
                if (byValue.TryGetValue(member.Value, out CExcelEnumMember other))
                {
                    issues.Add(Error(member.Row, def.Column,
                        $"枚举 {def.TypeName} 里 \"{other.Name}\"（{other.Where}）与 \"{member.Name}\"（{member.Where}）都等于 {member.Value} —— 同一枚举不允许两个成员同值，" +
                        $"请给其中一个显式指定别的值（写法：{member.RawName}_新值）。"));
                }
                else
                {
                    byValue[member.Value] = member;
                }
            }
        }

        // ========== 引用模式（_e:类型名） ==========

        private static void BuildExternal(CExcelEnumDef def, string typeName, IReadOnlyList<CExcelEnumCell> cells, List<CExcelIssue> issues)
        {
            if (!CExcelEnumResolver.TryResolve(typeName, out Type type, out string reason))
            {
                issues.Add(Error(0, def.Column,
                    $"引用的枚举类型 \"{typeName}\" 找不到：{reason}。引用的枚举必须**已经编译**在工程里（拼写、命名空间都要对；跨命名空间请写全名）。"));
                return;
            }

            def.ExternalType = type;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (CExcelEnumCell cell in cells)
            {
                string text = (cell.Raw ?? string.Empty).Trim();
                if (text.Length == 0) continue;

                foreach (string rawToken in SplitCellTokens(text, def.IsFlags, def.IsArray))
                {
                    string name = rawToken.Trim();
                    if (name.Length == 0) continue;

                    if (LooksLikeExplicitValue(name, out string bare, out long _))
                    {
                        issues.Add(Error(cell.Row, def.Column,
                            $"引用模式（{def.Column}）不支持 \"名字_值\" 显式写法：值由已编译的枚举 {typeName} 决定。这里应写 \"{bare}\"。"));
                        name = bare;
                    }

                    if (!seen.Add(name)) continue;

                    object value;
                    try
                    {
                        value = Enum.Parse(type, name, false);
                    }
                    catch (Exception)
                    {
                        issues.Add(Error(cell.Row, def.Column,
                            $"枚举 {typeName} 里没有成员 \"{name}\"（引用模式成员名要与已编译枚举完全一致，区分大小写）。现有成员：" + DescribeMembers(type)));
                        continue;
                    }

                    def.Members.Add(new CExcelEnumMember
                    {
                        RawName = name, Name = name, Value = Convert.ToInt64(value, CultureInfo.InvariantCulture), Row = cell.Row, Where = cell.Where, Explicit = true,
                    });
                }
            }

            if (def.Members.Count == 0)
            {
                issues.Add(Error(0, def.Column, $"枚举列没有任何取值（引用的是 {typeName}）。"));
            }
        }

        private static string DescribeMembers(Type type)
        {
            var sb = new StringBuilder();
            foreach (string name in Enum.GetNames(type))
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(name);
            }
            return sb.ToString();
        }

        // ========== 名字与值解析 ==========

        /// <summary>
        /// 一个单元格 → 成员名 token 列表。
        /// · 数组列（_ea / _flagsa）：先按 `;` `,` 拆元素
        /// · [Flags] 列（_flags / _flagsa）：每个元素再按 `|` 拆成员
        /// · 标量枚举（_e）：整格就是一个成员名
        /// </summary>
        public static List<string> SplitCellTokens(string text, bool isFlags, bool isArray)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return tokens;

            if (isArray) tokens.AddRange(CExcelTypeInfer.SplitArrayValue(text));
            else tokens.Add(text.Trim());

            if (!isFlags) return tokens;

            var expanded = new List<string>();
            foreach (string token in tokens)
                expanded.AddRange(CExcelTypeInfer.SplitOnAny(token, new[] { '|', '｜' }));
            return expanded;
        }

        /// <summary>
        /// 单元格文本 → 名字 + 可选显式值。规则：**按最后一个下划线拆**，
        /// 尾巴能当整数就当显式值，否则整串都是名字（这样 `fire_dragon` 不会被拆坏，`green_3` 会被拆）。
        /// </summary>
        public static bool SplitNameAndValue(string token, out string rawName, out long? value)
        {
            rawName = (token ?? string.Empty).Trim();
            value = null;
            if (rawName.Length == 0) return false;

            int underscore = rawName.LastIndexOf('_');
            if (underscore > 0 && underscore < rawName.Length - 1)
            {
                string tail = rawName.Substring(underscore + 1);
                if (long.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
                {
                    value = parsed;
                    rawName = rawName.Substring(0, underscore);
                }
            }
            return rawName.Length > 0;
        }

        /// <summary>是不是 <c>名字_整数</c> 形式（引用模式下用来给出更准确的报错）。</summary>
        public static bool LooksLikeExplicitValue(string token, out string bare, out long value)
        {
            long? parsed;
            bool ok = SplitNameAndValue(token, out bare, out parsed);
            value = parsed ?? 0;
            return ok && parsed.HasValue;
        }

        private static readonly HashSet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
            "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
            "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
            "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
            "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
            "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        };

        /// <summary>
        /// 取值 → 合法 C# 成员名：拆词转 PascalCase、非法字符换 _、数字开头补 _、关键字加 @。
        /// 取不出名字（全是符号）返回 null。
        /// </summary>
        public static string SanitizeMemberName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            var sb = new StringBuilder(raw.Length + 1);
            bool upperNext = true;
            foreach (char c in raw)
            {
                if (c == '_' || c == '-' || c == ' ' || c == '.')
                {
                    upperNext = true;
                    continue;
                }
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                    upperNext = false;
                }
                else
                {
                    sb.Append('_');
                    upperNext = true;
                }
            }

            string name = sb.ToString();
            if (name.Length == 0 || !HasLetterOrDigit(name)) return null;
            if (char.IsDigit(name[0])) name = "_" + name;
            // 防御性：C# 关键字全小写，而上面已经 PascalCase 过（首字母大写），所以实际到不了这里
            // （CExcelEnumDefTests.MemberName_Sanitization 记录了"实际不会触发"）
            if (CSharpKeywords.Contains(name)) name = "@" + name;
            return name;
        }

        private static bool HasLetterOrDigit(string text)
        {
            foreach (char c in text)
                if (char.IsLetterOrDigit(c)) return true;
            return false;
        }
    }

    /// <summary>
    /// 已编译枚举的查找（引用模式 <c>_e:类型名</c> 用）。
    /// 缓存结果：生成一张大表时同一类型会被问很多次。
    /// </summary>
    public static class CExcelEnumResolver
    {
        private static readonly Dictionary<string, Type> Found = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> Failed = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>清空缓存（域重载后本就会清；编译完新代码想立刻生效时手动调）。</summary>
        public static void ClearCache()
        {
            Found.Clear();
            Failed.Clear();
        }

        /// <summary>按名字找枚举类型：先全名精确匹配，再按简单名（唯一才算）。</summary>
        public static bool TryResolve(string name, out Type type, out string reason)
        {
            type = null;
            reason = null;
            if (string.IsNullOrEmpty(name))
            {
                reason = "类型名为空";
                return false;
            }

            if (Found.TryGetValue(name, out type)) return true;
            if (Failed.TryGetValue(name, out reason)) return false;

            var simpleMatches = new List<Type>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type exact = SafeGetType(assembly, name);
                if (exact != null && exact.IsEnum)
                {
                    type = exact;
                    Found[name] = exact;
                    return true;
                }

                foreach (Type candidate in SafeGetTypes(assembly))
                {
                    if (candidate == null || !candidate.IsEnum) continue;
                    if (string.Equals(candidate.Name, name, StringComparison.Ordinal)) simpleMatches.Add(candidate);
                }
            }

            if (simpleMatches.Count == 1)
            {
                type = simpleMatches[0];
                Found[name] = type;
                return true;
            }

            if (simpleMatches.Count == 0)
            {
                reason = "已加载的程序集里没有这个名字的枚举";
                Failed[name] = reason;
                return false;
            }

            var sb = new StringBuilder();
            foreach (Type match in simpleMatches)
            {
                if (sb.Length > 0) sb.Append("、");
                sb.Append(match.FullName);
            }
            reason = "有多个同名枚举，请写全名：" + sb;
            Failed[name] = reason;
            return false;
        }

        private static Type SafeGetType(Assembly assembly, string name)
        {
            try
            {
                return assembly.GetType(name, false);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                var list = new List<Type>();
                foreach (Type t in e.Types)
                    if (t != null) list.Add(t);
                return list;
            }
            catch (Exception)
            {
                return new List<Type>();
            }
        }
    }

    /// <summary>
    /// 一次生成里出现过的枚举类型登记表：**同名枚举的成员必须一致**，
    /// 否则生成的 C# 会撞类型（CS0101 重复定义）。跨表、跨文件都查。
    /// </summary>
    public sealed class CExcelEnumRegistry
    {
        private readonly Dictionary<string, string> _signatures = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _sources = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>登记一个枚举；同名但成员不一致时报错（返回 false）。</summary>
        public bool Register(CExcelEnumDef def, string sourceName, List<CExcelIssue> issues)
        {
            if (def == null || def.IsExternal) return true;

            string signature = def.Signature;
            if (_signatures.TryGetValue(def.TypeName, out string existing))
            {
                if (existing == signature) return true;
                issues.Add(new CExcelIssue
                {
                    Level = CExcelIssueLevel.Error,
                    Row = 0,
                    Column = def.Column,
                    Message = $"枚举类型名 {def.TypeName} 与 {_sources[def.TypeName]} 里生成的重名但成员不一致 —— 会生成重复的 C# 类型（CS0101）。" +
                              $"请改列名（换表名前缀）或让两边成员/取值完全一致。本次：{signature}",
                });
                return false;
            }

            _signatures[def.TypeName] = signature;
            _sources[def.TypeName] = sourceName + " 的 " + def.Column;
            return true;
        }
    }
}
