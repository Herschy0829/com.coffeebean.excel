using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CoffeeBean.Excel
{
    /// <summary>
    /// Excel 配置表工具窗口（Window &gt; CoffeeBean &gt; Excel Tools）：
    /// 选表 → 预览（表头 / 列类型 / 行数 / 问题）→ 生成（JSON + C# 类 + Getter）或批量生成目录。
    /// </summary>
    public sealed class CExcelToolsWindow : EditorWindow
    {
        private const string PrefExcelPath = "CoffeeBean.Excel.Path";
        private const string PrefOutputFolder = "CoffeeBean.Excel.OutputFolder";
        private const string PrefNamespace = "CoffeeBean.Excel.Namespace";

        private string _excelPath;
        private string _outputFolder = "Assets/Configs/Generated";
        private string _namespace = "Config";
        private string _className = string.Empty;
        private string _primaryKey = string.Empty;
        private bool _generateJson = true;
        private bool _generateClass = true;
        private bool _generateGetter = true;

        private CExcelReadResult _preview;
        private Vector2 _scroll;

        [MenuItem("Window/CoffeeBean/Excel Tools")]
        public static void Open() => GetWindow<CExcelToolsWindow>("Excel Tools");

        private void OnEnable()
        {
            _excelPath = EditorPrefs.GetString(PrefExcelPath, string.Empty);
            _outputFolder = EditorPrefs.GetString(PrefOutputFolder, "Assets/Configs/Generated");
            _namespace = EditorPrefs.GetString(PrefNamespace, "Config");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Excel 配置表工具", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // 表选择 + 预览
            EditorGUILayout.BeginHorizontal();
            _excelPath = EditorGUILayout.TextField("Excel 表", _excelPath);
            if (GUILayout.Button("选择", GUILayout.Width(60)))
            {
                string picked = EditorUtility.OpenFilePanel("选择配置表 Excel", "", "xlsx");
                if (!string.IsNullOrEmpty(picked))
                {
                    _excelPath = picked;
                    EditorPrefs.SetString(PrefExcelPath, _excelPath);
                    RefreshPreview();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("预览表", GUILayout.Height(24))) RefreshPreview();

            if (_preview != null)
            {
                EditorGUILayout.Space(4);
                DrawPreview(_preview);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("生成选项", EditorStyles.boldLabel);
            _outputFolder = EditorGUILayout.TextField("输出目录", _outputFolder);
            _namespace = EditorGUILayout.TextField("命名空间", _namespace);
            _className = EditorGUILayout.TextField("类名（空 = 表名）", _className);
            _primaryKey = EditorGUILayout.TextField("主键列（空 = 自动）", _primaryKey);
            EditorGUILayout.BeginHorizontal();
            _generateJson = EditorGUILayout.Toggle("生成 JSON", _generateJson);
            _generateClass = EditorGUILayout.Toggle("生成 C# 类", _generateClass);
            _generateGetter = EditorGUILayout.Toggle("生成 Getter", _generateGetter);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("生成当前表", GUILayout.Height(30))) GenerateSingle();
            if (GUILayout.Button("批量生成目录", GUILayout.Height(30))) GenerateFolder();
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("保存选项", GUILayout.Height(22)))
            {
                EditorPrefs.SetString(PrefOutputFolder, _outputFolder);
                EditorPrefs.SetString(PrefNamespace, _namespace);
                EditorPrefs.SetString(PrefExcelPath, _excelPath);
            }
        }

        // ===== 预览 =====

        private void RefreshPreview()
        {
            _preview = CExcelReader.Read(_excelPath);
            if (_preview.HasBlockingErrors)
            {
                ShowIssues(_preview.Issues, _preview.SourcePath);
                return;
            }

            // 预览列类型（按当前表数据推断）
            foreach (string column in _preview.Columns)
            {
                var values = new List<object>();
                foreach (Dictionary<string, object> row in _preview.Rows)
                    values.Add(row.TryGetValue(column, out object v) ? v : null);
                CExcelTypeInfer.Infer(column, values);
            }
        }

        private void DrawPreview(CExcelReadResult preview)
        {
            EditorGUILayout.LabelField("预览", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"表头行: 第 {preview.HeaderRowIndex + 1} 行   数据行: {preview.Rows.Count}   列: {preview.Columns.Count}");

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(150));
            foreach (string column in preview.Columns)
            {
                // 类型推断显示
                var values = new List<object>();
                foreach (Dictionary<string, object> row in preview.Rows)
                    values.Add(row.TryGetValue(column, out object v) ? v : null);
                CExcelFieldKind kind = CExcelTypeInfer.Infer(column, values);
                EditorGUILayout.LabelField($"  {column}  →  {CExcelTypeInfer.CSharpType(kind)}");
            }
            EditorGUILayout.EndScrollView();

            if (preview.Issues.Count > 0) ShowIssues(preview.Issues, preview.SourcePath);
        }

        private void ShowIssues(List<CExcelIssue> issues, string path)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("问题 (" + issues.Count + "):", EditorStyles.boldLabel);
            foreach (CExcelIssue issue in issues)
            {
                EditorGUILayout.HelpBox(issue.ToString(),
                    issue.Level == CExcelIssueLevel.Error ? MessageType.Error : MessageType.Warning);
            }
        }

        // ===== 生成 =====

        private void GenerateSingle()
        {
            if (string.IsNullOrEmpty(_excelPath))
            {
                EditorUtility.DisplayDialog("Excel Tools", "请先选择 Excel 表", "确定");
                return;
            }

            CExcelGenerateResult result = CExcelGenerator.Generate(_excelPath, BuildOptions());
            Report(result);
        }

        private void GenerateFolder()
        {
            string folder = EditorUtility.OpenFolderPanel("选择 Excel 目录", "", "");
            if (string.IsNullOrEmpty(folder)) return;

            CExcelGenerateResult result = CExcelGenerator.GenerateFolder(folder, BuildOptions());
            Report(result);
        }

        private CExcelGenerateOptions BuildOptions()
            => new CExcelGenerateOptions
            {
                OutputFolder = _outputFolder,
                Namespace = _namespace,
                ClassName = string.IsNullOrWhiteSpace(_className) ? null : _className,
                PrimaryKey = string.IsNullOrWhiteSpace(_primaryKey) ? null : _primaryKey,
                GenerateJson = _generateJson,
                GenerateClass = _generateClass,
                GenerateGetter = _generateGetter,
            };

        private void Report(CExcelGenerateResult result)
        {
            if (result.Success)
            {
                AssetDatabase.Refresh();
                var sb = new System.Text.StringBuilder();
                foreach (string file in result.GeneratedFiles) sb.AppendLine(file);
                EditorUtility.DisplayDialog("Excel Tools", "生成完成：\n" + sb, "确定");
                if (result.Issues.Count > 0) Debug.LogWarning("[CoffeeBean.Excel] 生成带警告:\n" + string.Join("\n", result.Issues));
            }
            else
            {
                EditorUtility.DisplayDialog("Excel Tools", "生成失败：\n" + string.Join("\n", result.Issues), "确定");
            }
        }
    }
}
