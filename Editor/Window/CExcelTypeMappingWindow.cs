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
    /// 也就是解析代码正在用的那份后缀表。加了新后缀，这里自动出现；
    /// 还有测试逐项核对（后缀往返、示例真实可解析、后缀之间无歧义），
    /// 所以"窗口说的"和"生成器做的"不可能对不上。
    ///
    /// 用法：查"这个列名该怎么写后缀 / 单元格里该怎么填 / 生成的字段长什么样"。
    /// </summary>
    [CoffeeBeanTool("类型映射说明", "列名后缀 → C# 类型对照表与单元格写法示例（含规划中类型）", "Excel")]
    public sealed class CExcelTypeMappingWindow : EditorWindow
    {
        private Vector2 _scroll;
        private bool _showPlanned = true;
        private bool _showRules = true;

        // 入口统一收敛到 CoffeeBean Hub（Window > CoffeeBean），不单独注册菜单项
        public static void Open() => GetWindow<CExcelTypeMappingWindow>("类型映射");

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Excel 列类型映射", new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
            EditorGUILayout.LabelField(
                "在列名末尾加类型后缀来声明类型（如 Level_i）。示例里的\"单元格写法\"就是该类型在 Excel 里的填法。",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(8);

            DrawSupportedTable();
            EditorGUILayout.Space(10);
            DrawRules();
            EditorGUILayout.Space(10);
            DrawPlanned();
            EditorGUILayout.Space(8);

            EditorGUILayout.HelpBox(
                "本窗口的内容由 CExcelTypeCatalog 渲染 —— 与实际解析用的是同一份后缀表，不会出现\"文档说支持、生成器不认\"。",
                MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        // ========== 已支持 ==========

        private void DrawSupportedTable()
        {
            EditorGUILayout.LabelField($"已支持（{CExcelTypeCatalog.All.Count} 种标量 + 对应数组）", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            DrawHeaderRow();
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                DrawSpecRow(spec.Suffix, spec.CSharpType, spec.Example, spec.Note);
                DrawSpecRow(spec.ArraySuffix, spec.ArrayCSharpType, spec.ArrayExample, "数组：分隔符 ; 或 ,（中文 ；， 也认）");
            }
        }

        private static void DrawHeaderRow()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("后缀", EditorStyles.miniBoldLabel, GUILayout.Width(56));
            GUILayout.Label("C# 类型", EditorStyles.miniBoldLabel, GUILayout.Width(84));
            GUILayout.Label("单元格写法", EditorStyles.miniBoldLabel, GUILayout.Width(190));
            GUILayout.Label("说明", EditorStyles.miniBoldLabel);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawSpecRow(string suffix, string type, string example, string note)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(suffix, EditorStyles.miniLabel, GUILayout.Width(56));
            GUILayout.Label(type, EditorStyles.miniLabel, GUILayout.Width(84));
            // 可选中的文本：方便直接复制示例去填表
            EditorGUILayout.SelectableLabel(example, EditorStyles.miniLabel,
                GUILayout.Width(190), GUILayout.Height(16));
            GUILayout.Label(note, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        // ========== 规则 ==========

        private void DrawRules()
        {
            _showRules = EditorGUILayout.Foldout(_showRules, "解析规则与边界", true);
            if (!_showRules) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            Label("列名 → 字段名", "去掉类型后缀后转 PascalCase：Level_i → Level、Reward_Items_sa → RewardItems");
            Label("无后缀怎么推断", "全列整数 → int；超 int32 → long；含小数 → double；整列 true/false → bool；否则 string。" +
                                    "⚠ 纯 1/0 的列会算 int（int 优先），想当 bool 就写 _b；浮点无后缀会落 double（没有 float 档，要 float 写 _f）");
            Label("后缀优先", "写了后缀就以后缀为准，不再看值（Name_s 即使整列是数字也是 string）");
            Label("大小写", "后缀匹配忽略大小写（Level_I 也认），但建议统一写小写");
            Label("数组分隔符", "; 或 ,（含中文 ；，）；空单元格 → 空数组");
            Label("空值", "数值空 → 0；字符串空 → 空串；数组空 → 空数组");
            Label("表头", "自动在前 3 行里找\"带后缀列名最多\"的那行当表头；它上方最近一行作为字段注释");

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
                $"规划中 / 需要新后端（{CExcelTypeCatalog.PlannedTypes.Count}）", true);
            if (!_showPlanned) return;

            EditorGUILayout.LabelField(
                "这些**还不能用**，列在这里是为了避免\"以为能填\"。统一卡在同一个点：生成的 JSON 目前由 JsonUtility 读，" +
                "它不支持 BigInteger / decimal / 时间 / 字典 / 嵌套数组；换 Newtonsoft（com.unity.nuget.newtonsoft-json）后可解锁。",
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
            sb.AppendLine("Excel 列类型映射");
            sb.AppendLine();
            sb.AppendLine("后缀\tC# 类型\t单元格写法\t说明");
            foreach (CExcelTypeSpec spec in CExcelTypeCatalog.All)
            {
                sb.AppendLine($"{spec.Suffix}\t{spec.CSharpType}\t{spec.Example}\t{spec.Note}");
                sb.AppendLine($"{spec.ArraySuffix}\t{spec.ArrayCSharpType}\t{spec.ArrayExample}\t数组：分隔符 ; 或 ,");
            }
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
