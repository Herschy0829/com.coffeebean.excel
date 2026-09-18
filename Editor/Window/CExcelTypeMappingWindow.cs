using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using CoffeeBean.EditorTools;

namespace CoffeeBean
{
    /// <summary>
    /// 列类型映射说明窗口（入口：Window &gt; CoffeeBean → Excel · 类型映射说明）。
    ///
    /// **它是活文档，不是手抄的表格**：内容全部来自 <see cref="CExcelTypeCatalog"/> ——
    /// 也就是解析/生成代码正在用的那份后缀表。加了新后缀，这里自动出现；
    /// 还有测试逐项核对（后缀往返、示例真能生成、生成的 JSON 真能被 Newtonsoft 读回），
    /// 所以"窗口说的"和"生成器做的"不可能对不上。
    ///
    /// 用法：查"这个列名该怎么写后缀 / 单元格里该怎么填 / 生成的字段长什么样"。
    /// </summary>
    [CoffeeBeanTool("类型映射说明", "列名后缀 → C# 类型对照表、单元格写法示例与枚举语法（含规划中类型）", "Excel")]
    public sealed class CExcelTypeMappingWindow : EditorWindow
    {
        private Vector2 _scroll;
        private bool _showPlanned = true;
        private bool _showRules = true;
        private bool _showEnum = true;
        private bool _showArrays;

        // 入口统一收敛到 CoffeeBean Hub（Window > CoffeeBean），不单独注册菜单项
        public static void Open() => GetWindow<CExcelTypeMappingWindow>("类型映射");

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Excel 列类型映射", new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
            EditorGUILayout.LabelField(
                "在列名末尾加类型后缀来声明类型（如 Level_i）。表里的\"单元格写法\"就是该类型在 Excel 里的填法，可直接选中复制。",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.HelpBox("JSON 后端：" + CExcelJsonBackend.Describe(),
                CExcelJsonBackend.IsAvailable ? MessageType.None : MessageType.Error);

            EditorGUILayout.Space(8);
            DrawSupportedTable();
            EditorGUILayout.Space(10);
            DrawEnumSection();
            EditorGUILayout.Space(10);
            DrawRules();
            EditorGUILayout.Space(10);
            DrawPlanned();
            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox(
                "本窗口的内容由 CExcelTypeCatalog 渲染 —— 与实际解析用的是同一份后缀表，不会出现\"文档说支持、生成器不认\"。",
                MessageType.None);
            if (GUILayout.Button("复制整张表\n（纯文本）", GUILayout.Width(110), GUILayout.Height(38)))
                EditorGUIUtility.systemCopyBuffer = ToPlainText();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
        }

        // ========== 已支持 ==========

        private void DrawSupportedTable()
        {
            EditorGUILayout.LabelField($"已支持（{CExcelTypeCatalog.All.Count} 种标量 + 各自数组后缀）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("数组后缀 = 标量后缀 + a（如 _i → _ia、_b → _ba、_e → _ea）；数组元素用 ; 或 , 分隔。",
                EditorStyles.wordWrappedMiniLabel);
            _showArrays = EditorGUILayout.ToggleLeft("同时列出数组后缀行", _showArrays);
            EditorGUILayout.Space(2);

            DrawHeaderRow();
            string currentGroup = null;
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                if (spec.Group != currentGroup)
                {
                    currentGroup = spec.Group;
                    EditorGUILayout.LabelField("— " + currentGroup + " —", EditorStyles.miniBoldLabel);
                }
                DrawSpecRow(spec.Suffix, spec.CSharpType, spec.Example,
                    spec.Note + (spec.NeedsNewtonsoft ? "  ⚙ JsonUtility 读不回这种类型（只有 Newtonsoft 后端能读）" : string.Empty));
                if (_showArrays)
                    DrawSpecRow(spec.ArraySuffix, spec.ArrayCSharpType, spec.ArrayExample, CExcelTypeCatalog.ArrayNote(spec.Kind));
            }
        }

        private static void DrawHeaderRow()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("后缀", EditorStyles.miniBoldLabel, GUILayout.Width(56));
            GUILayout.Label("C# 类型", EditorStyles.miniBoldLabel, GUILayout.Width(150));
            GUILayout.Label("单元格写法", EditorStyles.miniBoldLabel, GUILayout.Width(190));
            GUILayout.Label("说明", EditorStyles.miniBoldLabel);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawSpecRow(string suffix, string type, string example, string note)
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(suffix, EditorStyles.miniLabel, GUILayout.Width(56));
            GUILayout.Label(type, EditorStyles.miniLabel, GUILayout.Width(150));
            // 可选中的文本：方便直接复制示例去填表
            EditorGUILayout.SelectableLabel(example, EditorStyles.miniLabel,
                GUILayout.Width(190), GUILayout.Height(16));
            GUILayout.Label(note, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // ========== 枚举语法（最容易写错，单独一节） ==========

        private void DrawEnumSection()
        {
            _showEnum = EditorGUILayout.Foldout(_showEnum, "枚举怎么写（_e / _e:类型 / _flags）", true);
            if (!_showEnum) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            Label("两种模式", "_e = 让工具**生成**枚举类型；_e:已编译的枚举名 = **引用**工程里已有的枚举（不生成）。");
            Label("自动编号", "不带值的取值从 0 起按**首次出现顺序**编号：第一行 green → Green = 0，之后 blue → Blue = 1。");
            Label("显式值", "取值后面加 _数字：green_3 → Green = 3。之后出现的自动编号取\"已用最大值 + 1\"。");
            Label("拆分规则", "按**最后一个下划线**拆：fire_dragon 整个是名字；green_3 拆成 green + 值 3。");
            Label("生成的类型名", "表名 + 字段名：Building 表的 State_e → BuildingState。章节表各章节共用一套（取并集）。");
            Label("成员名", "自动转 PascalCase（green_leaf → GreenLeaf）；关键字自动加 @；引用模式则**要求与已编译名字完全一致**。");
            Label("校验（会报错）", "同一枚举两个成员同值 / 同名被赋两个不同的值 / 取值取不出合法成员名 / 引用的类型或成员找不到 / 枚举列一个取值都没有。");
            Label("JSON 里是什么", "枚举在 JSON 里存**数字**（成员值）。所以 JSON 不可读是有意的 —— 换来的是不用任何转换器、不怕改名。");
            Label("_flags", "位标记：单元格写 Fire|Ice（也认 , 和 ;），JSON 里存按位或后的数字。");
            Label("数组（_ea）", "元素用 ; 或 , 分隔：green;idle → [0,1]。_flagsa 是「先按 ; 拆元素、元素内部再用 | 组合」。");
            Label("空单元格", "空 → 默认值 0（枚举底层类型的零值）。");

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("例（Building 表）", EditorStyles.miniBoldLabel);
            EditorGUILayout.SelectableLabel(
                "列名:   State_e            Tags_flags:ElementFlags\n" +
                "第 1 行: green               Fire|Ice\n" +
                "第 2 行: idle                Poison\n" +
                "生成:    public enum BuildingState { Green = 0, Idle = 1 }\n" +
                "         public BuildingState State;\n" +
                "         public ElementFlags Tags;      // 引用模式，不生成枚举\n" +
                "JSON:    {\"State\":0,\"Tags\":5}",
                EditorStyles.textArea, GUILayout.Height(104));

            EditorGUILayout.EndVertical();
        }

        // ========== 规则 ==========

        private void DrawRules()
        {
            _showRules = EditorGUILayout.Foldout(_showRules, "解析规则与边界", true);
            if (!_showRules) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            Label("列名 → 字段名", "去掉类型后缀后转 PascalCase：Level_i → Level、Reward_Items_sa → RewardItems");
            Label("无后缀怎么推断", "全列整数 → int；超 int32 → long；**整数但超 long → string**（不猜 double，避免静默丢精度；要大整数请写 _b）；" +
                                    "含小数或指数 → double；整列 true/false → bool；否则 string。" +
                                    "⚠ 纯 1/0 的列会算 int（int 优先），想当 bool 就写 _bool；无后缀浮点会落 double（没有 float 档，要 float 写 _f）");
            Label("后缀优先", "写了后缀就以后缀为准，不再看值（Name_s 即使整列是数字也是 string）");
            Label("大小写", "后缀匹配忽略大小写（Level_I 也认），但建议统一写小写");
            Label("数组分隔符", "一般是 ; 或 ,（含中文 ；，）；**例外**：向量/颜色/矩形/四元数只能用 ;（元素内部是逗号），" +
                                "字典数组 _kva 用 |（组内键值对用 ; 或 ,）；空单元格 → 空数组");
            Label("空值", "数值空 → 0；字符串空 → 空串；字符空 → \\0；时间/标识空 → 零值；结构空 → 全 0；枚举空 → 0；数组空 → 空数组");
            Label("表头", "自动在前 3 行里找\"带后缀列名最多\"的那行当表头；它上方最近一行作为字段注释");
            Label("严格校验", "生成前每个单元格都按声明类型真解析：填错就直接报第几行第几列（不会像以前那样安静地写成 0）。" +
                              "\"生成选项\"里可关掉（StrictTypeCheck）");
            Label("大整数注意", "Excel 数字格式只保留 15 位有效数字。超过 15 位的大数（_b）必须把单元格设成**文本格式**，" +
                                "否则会变科学计数法 —— 生成时会直接报错告诉你。");

            EditorGUILayout.EndVertical();
        }

        private static void Label(string title, string detail)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(title, EditorStyles.miniBoldLabel, GUILayout.Width(110));
            GUILayout.Label(detail, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndHorizontal();
        }

        // ========== 规划中 ==========

        private void DrawPlanned()
        {
            _showPlanned = EditorGUILayout.Foldout(_showPlanned,
                $"规划中 / 还没支持（{CExcelTypeCatalog.PlannedTypes.Count}）", true);
            if (!_showPlanned) return;

            EditorGUILayout.LabelField(
                "这些**还不能用**，列在这里是为了避免\"以为能填\"。",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            foreach (CExcelPlannedType planned in CExcelTypeCatalog.PlannedTypes)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(planned.Suffix, EditorStyles.miniBoldLabel, GUILayout.Width(120));
                GUILayout.Label(planned.CSharpType, EditorStyles.miniLabel, GUILayout.Width(220));
                EditorGUILayout.SelectableLabel(planned.Example, EditorStyles.miniLabel, GUILayout.Height(16));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("· " + planned.Blocker, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
            }
        }

        /// <summary>把整张表导成纯文本（贴到文档/群里用）。</summary>
        public static string ToPlainText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Excel 列类型映射（后端：" + CExcelJsonBackend.Describe() + "）");
            sb.AppendLine();
            sb.AppendLine("后缀\tC# 类型\t单元格写法\t说明");
            string currentGroup = null;
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                if (spec.Group != currentGroup)
                {
                    currentGroup = spec.Group;
                    sb.AppendLine("-- " + currentGroup + " --");
                }
                sb.AppendLine($"{spec.Suffix}\t{spec.CSharpType}\t{spec.Example}\t{spec.Note}");
                sb.AppendLine($"{spec.ArraySuffix}\t{spec.ArrayCSharpType}\t{spec.ArrayExample}\t{CExcelTypeCatalog.ArrayNote(spec.Kind)}");
            }

            sb.AppendLine();
            sb.AppendLine("枚举语法：");
            sb.AppendLine("_e\t枚举（表内生成）\tgreen 或 green_3\t不带值从 0 起自动编号；green_3 = 显式值 3；同一枚举不允许两个成员同值");
            sb.AppendLine("_e:类型\t已编译的枚举\tgreen\t引用已有枚举，成员名要与编译好的完全一致");
            sb.AppendLine("_flags\t[Flags] 枚举\tFire|Ice\t位标记，JSON 存按位或的数字");
            sb.AppendLine("_flags:类型\t[Flags] 已编译枚举\tFire|Ice\t同上，引用已有枚举");

            sb.AppendLine();
            sb.AppendLine("规划中（暂不可用）：");
            foreach (CExcelPlannedType planned in CExcelTypeCatalog.PlannedTypes)
            {
                sb.AppendLine($"{planned.Suffix}\t{planned.CSharpType}\t{planned.Example}\t{planned.Blocker}");
            }
            return sb.ToString();
        }
    }
}
