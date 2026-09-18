using System.Collections.Generic;
using NUnit.Framework;

namespace CoffeeBean.Excel.Tests
{
    /// <summary>
    /// 枚举构建测试（`_e` / `_e:类型` / `_flags`）：
    /// 自动编号、显式值、成员名规范化，以及用户点名要的**"不能有同一枚举值"**等全部校验。
    /// </summary>
    public class CExcelEnumDefTests
    {
        private static readonly CExcelFieldKind EnumKind = CExcelFieldKind.Enum;
        private static readonly CExcelFieldKind FlagsKind = CExcelFieldKind.Flags;

        private static CExcelEnumDef Build(string column, IEnumerable<string> cells,
            CExcelFieldKind kind = CExcelFieldKind.Enum, string prefix = "Building", List<CExcelIssue> issues = null)
        {
            var list = new List<CExcelEnumCell>();
            int row = 2;
            foreach (string cell in cells) list.Add(new CExcelEnumCell(cell, row++));
            return CExcelEnumBuilder.Build(column, kind, false, prefix, list, issues ?? new List<CExcelIssue>());
        }

        // ========== 自动编号 / 显式值 ==========

        [Test]
        public void AutoNumbering_FollowsFirstAppearanceOrderFromZero()
        {
            CExcelEnumDef def = Build("State_e", new[] { "green", "idle", "green" });

            Assert.AreEqual("BuildingState", def.TypeName, "类型名 = 表名 + 字段名");
            Assert.AreEqual(2, def.Members.Count, "同一个取值只算一个成员");
            Assert.AreEqual("Green", def.Members[0].Name);
            Assert.AreEqual(0, def.Members[0].Value);
            Assert.AreEqual("Idle", def.Members[1].Name);
            Assert.AreEqual(1, def.Members[1].Value);
        }

        [Test]
        public void ExplicitValue_UsesUnderscoreSuffix_AndAutoContinuesAfterMax()
        {
            CExcelEnumDef def = Build("State_e", new[] { "green_3", "idle", "blue" });

            Assert.AreEqual(3, def.Members[0].Value);
            Assert.IsTrue(def.Members[0].Explicit);
            Assert.AreEqual(4, def.Members[1].Value, "自动编号 = 已用最大值 + 1");
            Assert.AreEqual(5, def.Members[2].Value);
        }

        [Test]
        public void ExplicitNegativeValue_DoesNotBreakAutoNumbering()
        {
            CExcelEnumDef def = Build("State_e", new[] { "none_-1", "green" });
            Assert.AreEqual(-1, def.Members[0].Value);
            Assert.AreEqual(0, def.Members[1].Value);
        }

        /// <summary>**按最后一个下划线拆**：名字里的下划线不能被当成分隔符。</summary>
        [Test]
        public void NameWithUnderscore_IsNotSplit_UnlessTailIsAnInteger()
        {
            CExcelEnumDef def = Build("State_e", new[] { "fire_dragon", "green_3" });
            Assert.AreEqual("fire_dragon", def.Members[0].RawName);
            Assert.AreEqual("FireDragon", def.Members[0].Name);
            Assert.AreEqual(0, def.Members[0].Value);
            Assert.AreEqual("green", def.Members[1].RawName);
            Assert.AreEqual(3, def.Members[1].Value);
        }

        // ========== 校验：不能有同一枚举值 ==========

        [Test]
        public void TwoMembersWithSameValue_IsAnError()
        {
            var issues = new List<CExcelIssue>();
            Build("State_e", new[] { "green_1", "blue_1" }, issues: issues);

            Assert.IsTrue(issues.Exists(i => i.Level == CExcelIssueLevel.Error && i.Message.Contains("同值")),
                "同一枚举不允许两个成员同值 —— 必须报错。实际：" + string.Join(" | ", issues));
        }

        [Test]
        public void SameNameWithTwoDifferentValues_IsAnError()
        {
            var issues = new List<CExcelIssue>();
            Build("State_e", new[] { "green_1", "green_2" }, issues: issues);

            Assert.IsTrue(issues.Exists(i => i.Level == CExcelIssueLevel.Error && i.Message.Contains("两个不同的值")),
                "同名不同值必须报错。实际：" + string.Join(" | ", issues));
        }

        [Test]
        public void SameNameWithSameValue_IsFine()
        {
            var issues = new List<CExcelIssue>();
            CExcelEnumDef def = Build("State_e", new[] { "green", "green_0" }, issues: issues);

            Assert.AreEqual(1, def.Members.Count);
            Assert.IsFalse(issues.Exists(i => i.Level == CExcelIssueLevel.Error), "同名同值只是在重复用同一个成员，不该报错");
        }

        [Test]
        public void EmptyEnumColumn_IsAnError()
        {
            var issues = new List<CExcelIssue>();
            Build("State_e", new[] { "", "  ", null }, issues: issues);
            Assert.IsTrue(issues.Exists(i => i.Level == CExcelIssueLevel.Error), "枚举列一个取值都没有 → C# 枚举不能为空");
        }

        // ========== 成员名规范化 ==========

        [Test]
        public void MemberName_Sanitization()
        {
            Assert.AreEqual("GreenLeaf", CExcelEnumBuilder.SanitizeMemberName("green_leaf"));
            Assert.AreEqual("GreenLeaf", CExcelEnumBuilder.SanitizeMemberName("green leaf"));
            Assert.AreEqual("GreenLeaf", CExcelEnumBuilder.SanitizeMemberName("green-leaf"));
            Assert.AreEqual("_123", CExcelEnumBuilder.SanitizeMemberName("123"), "数字开头要补下划线");
            Assert.AreEqual("A1", CExcelEnumBuilder.SanitizeMemberName("a1"));
            Assert.IsNull(CExcelEnumBuilder.SanitizeMemberName("___"), "取不出名字（没有字母/数字）应返回 null");
            Assert.IsNull(CExcelEnumBuilder.SanitizeMemberName("!!!"));

            // C# 关键字全小写，而 PascalCase 会把首字母大写 —— 所以关键字根本活不下来。
            // （SanitizeMemberName 里仍保留 @ 前缀逻辑作防御，这条测试记录"实际不会触发"。）
            Assert.AreEqual("Class", CExcelEnumBuilder.SanitizeMemberName("class"));
            Assert.AreEqual("Int", CExcelEnumBuilder.SanitizeMemberName("int"));
        }

        [Test]
        public void SanitizedNameChange_IsAWarningSoUserSeesTheRealName()
        {
            var issues = new List<CExcelIssue>();
            CExcelEnumDef def = Build("State_e", new[] { "green_leaf" }, issues: issues);

            Assert.AreEqual("GreenLeaf", def.Members[0].Name);
            Assert.IsTrue(issues.Exists(i => i.Level == CExcelIssueLevel.Warning && i.Message.Contains("GreenLeaf")),
                "名字被改了要提醒，否则用户按原样写代码会编译不过");
            Assert.IsFalse(issues.Exists(i => i.Level == CExcelIssueLevel.Error));
        }

        // ========== 数组 / Flags ==========

        [Test]
        public void EnumArray_KindIsArray()
        {
            var list = new List<CExcelEnumCell> { new CExcelEnumCell("green;idle", 2) };
            var def = CExcelEnumBuilder.Build("State_ea", CExcelFieldKind.Enum, true, "Building", list, new List<CExcelIssue>());
            Assert.IsTrue(def.IsArray);
            Assert.AreEqual("BuildingState", def.TypeName, "数组不加后缀到类型名上，字段声明时才加 []");
        }

        [Test]
        public void Flags_MembersCombineWithBitwiseOr()
        {
            CExcelEnumDef def = Build("Tags_flags", new[] { "Fire", "Ice", "Fire|Ice" }, FlagsKind);

            Assert.IsTrue(def.IsFlags);
            Assert.AreEqual(0, def.Members[0].Value);
            Assert.AreEqual(1, def.Members[1].Value);

            Assert.IsTrue(def.TryResolveFlags("Fire|Ice", out long both, out string error), error);
            Assert.AreEqual(1, both, "0 | 1 = 1");
            Assert.IsTrue(def.TryResolveFlags("Ice", out long ice, out _));
            Assert.AreEqual(1, ice);
            Assert.IsTrue(def.TryResolveFlags("Fire,Ice", out long comma, out _), "逗号也认");
            Assert.AreEqual(1, comma);
        }

        [Test]
        public void Flags_DuplicateValueIsStillAnError()
        {
            var issues = new List<CExcelIssue>();
            Build("Tags_flags", new[] { "Fire_0", "Ice_0" }, FlagsKind, issues: issues);
            Assert.IsTrue(issues.Exists(i => i.Level == CExcelIssueLevel.Error && i.Message.Contains("同值")));
        }

        // ========== 引用模式（_e:已编译的枚举） ==========

        [Test]
        public void ExternalMode_ResolvesCompiledEnumAndItsValues()
        {
            var issues = new List<CExcelIssue>();
            CExcelEnumDef def = Build("Day_e:System.DayOfWeek", new[] { "Monday", "Friday", "Monday" }, issues: issues);

            Assert.IsFalse(issues.Exists(i => i.Level == CExcelIssueLevel.Error), string.Join(" | ", issues));
            Assert.IsTrue(def.IsExternal);
            Assert.AreEqual("System.DayOfWeek", def.TypeName);
            Assert.IsNotNull(def.ExternalType);
            Assert.AreEqual(2, def.Members.Count);
            Assert.IsTrue(def.TryResolve("Monday", out long monday, out _));
            Assert.AreEqual((long)System.DayOfWeek.Monday, monday);
        }

        /// <summary>引用模式：成员名必须与已编译枚举**完全一致**（不做 PascalCase）。</summary>
        [Test]
        public void ExternalMode_MemberNameMustMatchExactly()
        {
            var issues = new List<CExcelIssue>();
            Build("Day_e:System.DayOfWeek", new[] { "monday" }, issues: issues);

            Assert.IsTrue(issues.Exists(i => i.Level == CExcelIssueLevel.Error && i.Message.Contains("没有成员")),
                "外部枚举里没有小写 monday，必须报错并列出实际成员：" + string.Join(" | ", issues));
            Assert.IsTrue(issues.Exists(i => i.Message.Contains("Monday")), "报错要列出可用成员");
        }

        [Test]
        public void ExternalMode_UnknownType_IsAnError()
        {
            var issues = new List<CExcelIssue>();
            Build("Day_e:NoSuchEnumAnywhere", new[] { "Monday" }, issues: issues);
            Assert.IsTrue(issues.Exists(i => i.Level == CExcelIssueLevel.Error && i.Message.Contains("找不到")));
        }

        [Test]
        public void ExternalMode_ExplicitValueSyntax_IsAnError()
        {
            var issues = new List<CExcelIssue>();
            Build("Day_e:System.DayOfWeek", new[] { "Monday_3" }, issues: issues);
            Assert.IsTrue(issues.Exists(i => i.Level == CExcelIssueLevel.Error && i.Message.Contains("显式")),
                "外部枚举的值由枚举决定，不该允许 名字_值 写法：" + string.Join(" | ", issues));
        }

        // ========== 跨表重名登记 ==========

        [Test]
        public void Registry_DetectsSameEnumNameWithDifferentMembers()
        {
            var registry = new CExcelEnumRegistry();
            var issues = new List<CExcelIssue>();

            CExcelEnumDef first = Build("State_e", new[] { "green", "idle" }, prefix: "Building");
            Assert.IsTrue(registry.Register(first, "Building", issues));

            CExcelEnumDef same = Build("State_e", new[] { "green", "idle" }, prefix: "Building");
            Assert.IsTrue(registry.Register(same, "Other", issues), "成员完全一致 → 不算冲突");

            CExcelEnumDef different = Build("State_e", new[] { "red", "blue" }, prefix: "Building");
            Assert.IsFalse(registry.Register(different, "Other", issues), "同名不同成员 → 会生成重复的 C# 类型，必须拦下");
            Assert.IsTrue(issues.Exists(i => i.Level == CExcelIssueLevel.Error && i.Message.Contains("重名")));
        }

        [Test]
        public void Registry_IgnoresExternalEnums()
        {
            var registry = new CExcelEnumRegistry();
            var issues = new List<CExcelIssue>();
            CExcelEnumDef external = Build("Day_e:System.DayOfWeek", new[] { "Monday" }, issues: issues);
            Assert.IsTrue(registry.Register(external, "Any", issues), "引用的是已编译枚举，不生成类型，不参与重名检查");
        }

        /// <summary>章节表：各章节取值不同也要共用一套编号，不能各建一套。</summary>
        [Test]
        public void ChapterUnion_SharedNumberingAcrossSheets()
        {
            var cells = new List<CExcelEnumCell>
            {
                new CExcelEnumCell("green", 2, "ChapterConfig_1"),
                new CExcelEnumCell("green", 3, "ChapterConfig_1"),
                new CExcelEnumCell("blue", 2, "ChapterConfig_2"),
                new CExcelEnumCell("green", 5, "ChapterConfig_2"),
            };
            var issues = new List<CExcelIssue>();
            CExcelEnumDef def = CExcelEnumBuilder.Build("State_e", CExcelFieldKind.Enum, false, "ChapterConfig", cells, issues);

            Assert.IsFalse(issues.Exists(i => i.Level == CExcelIssueLevel.Error), string.Join(" | ", issues));
            Assert.AreEqual(2, def.Members.Count);
            Assert.AreEqual(0, def.Members[0].Value, "green 在章节 1 首次出现 → 0");
            Assert.AreEqual(1, def.Members[1].Value, "blue 在章节 2 出现 → 1（按并集顺序编号）");
            Assert.AreEqual("ChapterConfigState", def.TypeName, "章节枚举用章节前缀命名，各章节共用");
        }

        [Test]
        public void ChapterUnion_ErrorMessagesNameTheSheet()
        {
            var cells = new List<CExcelEnumCell>
            {
                new CExcelEnumCell("green_1", 2, "ChapterConfig_1"),
                new CExcelEnumCell("blue_1", 3, "ChapterConfig_2"),
            };
            var issues = new List<CExcelIssue>();
            CExcelEnumBuilder.Build("State_e", CExcelFieldKind.Enum, false, "ChapterConfig", cells, issues);

            CExcelIssue error = issues.Find(i => i.Level == CExcelIssueLevel.Error);
            Assert.IsNotNull(error);
            StringAssert.Contains("ChapterConfig_1", error.Message);
            StringAssert.Contains("ChapterConfig_2", error.Message);
        }
    }
}
