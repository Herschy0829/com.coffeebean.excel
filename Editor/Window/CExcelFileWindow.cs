using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CoffeeBean.Excel
{
    /// <summary>
    /// 单文件校验 / 预览二级窗口：选择 sheet → 预览（表头 / 列类型 / 行数 / 问题）→ 校验 / 生成。
    /// 由主窗口（<see cref="CExcelToolsWindow"/>）表列表的"预览/校验"按钮打开。
    /// </summary>
    public sealed class CExcelFileWindow : EditorWindow
    {
        private string _path;
        private List<string> _sheets = new List<string>();
        private int _sheetIndex;
        private CExcelReadResult _preview;
        private Vector2 _scroll;

        public static void Open(string excelPath)
        {
            var window = GetWindow<CExcelFileWindow>("Excel 预览/校验");
            window._path = excelPath;
            window.RefreshSheets();
            window.RefreshPreview();
        }

        private void RefreshSheets()
        {
            _sheets = CExcelReader.GetSheetNames(_path);
            _sheetIndex = 0;
        }

        private void RefreshPreview()
        {
            if (_sheetIndex < 0 || _sheetIndex >= _sheets.Count) return;
            _preview = CExcelReader.Read(_path, new CExcelReadOptions { SheetName = _sheets[_sheetIndex] });
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("单文件校验 / 预览", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("文件: " + _path);
            EditorGUILayout.Space(4);

            if (_sheets.Count == 0)
            {
                EditorGUILayout.HelpBox("无可用 sheet", MessageType.Warning);
                return;
            }

            // sheet 选择
            EditorGUILayout.BeginHorizontal();
            int newIndex = EditorGUILayout.Popup("Sheet", _sheetIndex, _sheets.ToArray());
            if (newIndex != _sheetIndex)
            {
                _sheetIndex = newIndex;
                RefreshPreview();
            }
            if (GUILayout.Button("预览", GUILayout.Width(60))) RefreshPreview();
            EditorGUILayout.EndHorizontal();

            if (_preview == null) return;

            // 表头 / 行数 / 问题
            EditorGUILayout.LabelField($"表头行: 第 {_preview.HeaderRowIndex + 1} 行   数据行: {_preview.Rows.Count}   列: {_preview.Columns.Count}");
            if (_preview.HasBlockingErrors)
                EditorGUILayout.HelpBox("存在阻塞性错误，无法生成", MessageType.Error);

            // 列 + 类型
            EditorGUILayout.LabelField("列（列名 → 类型）", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(180));
            foreach (string column in _preview.Columns)
            {
                var values = new List<object>();
                foreach (Dictionary<string, object> row in _preview.Rows)
                    values.Add(row.TryGetValue(column, out object v) ? v : null);
                CExcelFieldKind kind = CExcelTypeInfer.Infer(column, values);
                string comment = _preview.ColumnComments.TryGetValue(column, out string c) ? c : string.Empty;
                EditorGUILayout.LabelField($"  {column}  →  {CExcelTypeInfer.CSharpType(kind)}" + (comment.Length > 0 ? "    （" + comment + "）" : ""));
            }
            EditorGUILayout.EndScrollView();

            // 问题列表
            if (_preview.Issues.Count > 0)
            {
                EditorGUILayout.LabelField($"问题（{_preview.Issues.Count}）", EditorStyles.boldLabel);
                foreach (CExcelIssue issue in _preview.Issues)
                {
                    EditorGUILayout.HelpBox(issue.ToString(),
                        issue.Level == CExcelIssueLevel.Error ? MessageType.Error : MessageType.Warning);
                }
            }

            // 操作
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("校验（不生成）", GUILayout.Height(26))) RefreshPreview();
            if (GUILayout.Button("生成此 sheet", GUILayout.Height(26))) GenerateCurrent();
            EditorGUILayout.EndHorizontal();
        }

        private void GenerateCurrent()
        {
            if (_preview == null || _preview.HasBlockingErrors)
            {
                EditorUtility.DisplayDialog("Excel 预览/校验", "存在阻塞性错误，无法生成（见问题列表）", "确定");
                return;
            }

            var options = new CExcelGenerateOptions
            {
                OutputFolder = EditorPrefs.GetString("CoffeeBean.Excel.OutputFolder", "Assets/Configs/Generated"),
                Namespace = EditorPrefs.GetString("CoffeeBean.Excel.Namespace", "Config"),
                SheetName = _sheets[_sheetIndex],
                JsonResourcesFolder = EditorPrefs.GetString("CoffeeBean.Excel.JsonResourcesFolder", "Assets/Resources/Configs"),
                ResourcesPath = EditorPrefs.GetString("CoffeeBean.Excel.ResourcesPath", "Configs"),
                EncryptJson = EditorPrefs.GetBool("CoffeeBean.Excel.EncryptJson", true),
            };
            CExcelGenerateResult result = CExcelGenerator.Generate(_path, options);
            if (result.Success)
            {
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Excel 预览/校验", "生成完成：\n" + string.Join("\n", result.GeneratedFiles), "确定");
            }
            else
            {
                EditorUtility.DisplayDialog("Excel 预览/校验", "生成失败：\n" + string.Join("\n", result.Issues), "确定");
            }
        }
    }
}
