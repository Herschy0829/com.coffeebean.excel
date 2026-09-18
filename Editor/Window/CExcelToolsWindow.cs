using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using CoffeeBean.EditorTools;

namespace CoffeeBean
{
    /// <summary>
    /// Excel 配置表工具主窗口（入口：Window &gt; CoffeeBean &gt; Excel Tools 收敛到 CoffeeBean Hub）：
    /// **文件夹批量生成**为主界面——选择一次文件夹（EditorPrefs 记忆）后一键增量生成；
    /// 单文件校验 / 预览在二级窗口（<see cref="CExcelFileWindow"/>，列表行"预览"按钮打开）。
    /// 增量：只重新生成修改过的表（<see cref="CExcelIncrementalGenerator"/> 记录文件修改时间）。
    /// </summary>
    [CoffeeBeanTool("Excel 配置表工具", "文件夹批量生成 / 单表校验预览 / 增量生成（C# 类 + JSON）", "Excel")]
    public sealed class CExcelToolsWindow : EditorWindow
    {
        private const string PrefFolder = "CoffeeBean.Excel.Folder";
        private const string PrefOutputFolder = "CoffeeBean.Excel.OutputFolder";
        private const string PrefNamespace = "CoffeeBean.Excel.Namespace";
        private const string PrefJsonResources = "CoffeeBean.Excel.JsonResourcesFolder";
        private const string PrefResourcesPath = "CoffeeBean.Excel.ResourcesPath";
        private const string PrefEncryptJson = "CoffeeBean.Excel.EncryptJson";
        private const string PrefStrictTypeCheck = "CoffeeBean.Excel.StrictTypeCheck";
        private const string PrefArraySeparators = "CoffeeBean.Excel.ArraySeparators";
        private const string PrefSkipRowsWithoutKey = "CoffeeBean.Excel.SkipRowsWithoutKey";

        private string _folder;
        private string _outputFolder = "Assets/Configs/Generated";
        private string _namespace = "CoffeeBean";
        private string _jsonResourcesFolder = "Assets/Resources/Configs";
        private string _resourcesPath = "Configs";
        private string _primaryKey = string.Empty;
        private bool _generateJson = true;
        private bool _generateClass = true;
        private bool _encryptJson = true;
        private bool _strictTypeCheck = true;
        private string _arraySeparators = CExcelCellJson.DefaultArraySeparators;
        private bool _skipRowsWithoutKey = true;

        private readonly List<FileStatus> _files = new List<FileStatus>();
        private Vector2 _scroll;
        private string _summary = "";

        private sealed class FileStatus
        {
            public string Path;
            public string Name;
            public string State = "";   // "generated" / "skipped" / "failed" / ""
            public string Detail = "";
        }

        // 入口统一收敛到 CoffeeBean Hub（Window > CoffeeBean），不再单独注册菜单项
        public static void Open() => GetWindow<CExcelToolsWindow>("Excel Tools");

        private void OnEnable()
        {
            _folder = EditorPrefs.GetString(PrefFolder, string.Empty);
            _outputFolder = EditorPrefs.GetString(PrefOutputFolder, "Assets/Configs/Generated");
            _namespace = EditorPrefs.GetString(PrefNamespace, "CoffeeBean");
            _jsonResourcesFolder = EditorPrefs.GetString(PrefJsonResources, "Assets/Resources/Configs");
            _resourcesPath = EditorPrefs.GetString(PrefResourcesPath, "Configs");
            _encryptJson = EditorPrefs.GetBool(PrefEncryptJson, true);
            _strictTypeCheck = EditorPrefs.GetBool(PrefStrictTypeCheck, true);
            _arraySeparators = EditorPrefs.GetString(PrefArraySeparators, CExcelCellJson.DefaultArraySeparators);
            _skipRowsWithoutKey = EditorPrefs.GetBool(PrefSkipRowsWithoutKey, true);
            RefreshFileList();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Excel 配置表工具（文件夹批量生成）", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // 文件夹（一次设置，之后记住）
            EditorGUILayout.BeginHorizontal();
            _folder = EditorGUILayout.TextField("Excel 文件夹", _folder);
            if (GUILayout.Button("选择", GUILayout.Width(60)))
            {
                string picked = EditorUtility.OpenFolderPanel("选择配置表 Excel 文件夹", _folder, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    _folder = picked;
                    EditorPrefs.SetString(PrefFolder, _folder);
                    RefreshFileList();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("生成全部（增量）", GUILayout.Height(28))) GenerateAll(force: false);
            if (GUILayout.Button("强制重新生成", GUILayout.Height(28))) GenerateAll(force: true);
            if (GUILayout.Button("清空状态", GUILayout.Height(28)))
            {
                CExcelIncrementalGenerator.Clear();
                RefreshFileList();
            }
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_summary))
                EditorGUILayout.HelpBox(_summary, MessageType.Info);

            // 生成选项
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("生成选项", EditorStyles.boldLabel);
            _outputFolder = EditorGUILayout.TextField("代码输出目录", _outputFolder);
            _jsonResourcesFolder = EditorGUILayout.TextField("JSON Resources 目录", _jsonResourcesFolder);
            _resourcesPath = EditorGUILayout.TextField("Resources 相对路径", _resourcesPath);
            _namespace = EditorGUILayout.TextField("命名空间", _namespace);
            _primaryKey = EditorGUILayout.TextField("主键列（空 = 自动）", _primaryKey);
            EditorGUILayout.BeginHorizontal();
            _generateJson = EditorGUILayout.Toggle("生成 JSON", _generateJson);
            _generateClass = EditorGUILayout.Toggle("生成 C# 类 + Getter", _generateClass);
            _encryptJson = EditorGUILayout.Toggle("加密 JSON", _encryptJson);
            EditorGUILayout.EndHorizontal();

            // 严格类型校验：生成前每格按声明类型真解析，填错直接报第几行第几列（不再安静地写成 0）
            _strictTypeCheck = EditorGUILayout.ToggleLeft(
                "严格类型校验（推荐）—— 值不符合列类型就报错中止该表，不静默写 0", _strictTypeCheck);

            // 数组分隔符：默认带 `_`（真实项目里 13_100 / 0.2_0.8_1 这类写法最常见）
            EditorGUILayout.BeginHorizontal();
            _arraySeparators = EditorGUILayout.TextField("数组分隔符", _arraySeparators);
            if (GUILayout.Button("恢复默认", GUILayout.Width(70)))
                _arraySeparators = CExcelCellJson.DefaultArraySeparators;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                "   数组元素分隔符（默认含 `_`：13_100 = [13,100]）。字符串数组的元素本身带下划线时，把 `_` 去掉。" +
                "枚举数组永远不按 `_` 拆（`_` 是 \"名字_值\" 语法）。",
                EditorStyles.wordWrappedMiniLabel);

            // 主键无效行（说明行/图例行/草稿块）跳过 —— 会出警告并列出跳过的行号
            _skipRowsWithoutKey = EditorGUILayout.ToggleLeft(
                "跳过主键无效的行（推荐）—— 配置表里的说明/图例/草稿行主键是空的，不当数据行（会出警告列出行号）",
                _skipRowsWithoutKey);

            // 生成代码用 Newtonsoft 反序列化，缺包会编译不过 —— 提前告知
            if (!CExcelJsonBackend.IsAvailable)
            {
                EditorGUILayout.HelpBox("JSON 后端：" + CExcelJsonBackend.Describe(), MessageType.Error);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("打开 Package Manager", GUILayout.Height(22)))
                    EditorApplication.ExecuteMenuItem("Window/Package Manager");
                if (GUILayout.Button("复制包名", GUILayout.Width(90), GUILayout.Height(22)))
                    EditorGUIUtility.systemCopyBuffer = CExcelJsonBackend.PackageName;
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("保存选项", GUILayout.Height(22)))
            {
                EditorPrefs.SetString(PrefFolder, _folder);
                EditorPrefs.SetString(PrefOutputFolder, _outputFolder);
                EditorPrefs.SetString(PrefNamespace, _namespace);
                EditorPrefs.SetString(PrefJsonResources, _jsonResourcesFolder);
                EditorPrefs.SetString(PrefResourcesPath, _resourcesPath);
                EditorPrefs.SetBool(PrefEncryptJson, _encryptJson);
                EditorPrefs.SetBool(PrefStrictTypeCheck, _strictTypeCheck);
                EditorPrefs.SetString(PrefArraySeparators, _arraySeparators);
                EditorPrefs.SetBool(PrefSkipRowsWithoutKey, _skipRowsWithoutKey);
            }

            // 表状态列表
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("配置表（" + _files.Count + "）", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(240));
            foreach (FileStatus file in _files)
            {
                EditorGUILayout.BeginHorizontal();
                string stateIcon = file.State switch
                {
                    "generated" => "✓ 已生成",
                    "skipped" => "- 未变化跳过",
                    "failed" => "✗ 失败",
                    _ => "   ",
                };
                GUILayout.Label(stateIcon, GUILayout.Width(100));
                GUILayout.Label(file.Name);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("预览/校验", GUILayout.Width(80))) CExcelFileWindow.Open(file.Path);
                EditorGUILayout.EndHorizontal();
                if (file.State == "failed" && file.Detail.Length > 0)
                    EditorGUILayout.HelpBox(file.Detail, MessageType.Error);
            }
            EditorGUILayout.EndScrollView();
        }

        // ===== 批量生成 =====

        private void RefreshFileList()
        {
            _files.Clear();
            if (string.IsNullOrEmpty(_folder) || !Directory.Exists(_folder)) return;

            foreach (string file in Directory.GetFiles(_folder, "*.xlsx"))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith("~$", System.StringComparison.Ordinal)) continue;
                _files.Add(new FileStatus { Path = file, Name = name });
            }
        }

        private void GenerateAll(bool force)
        {
            if (string.IsNullOrEmpty(_folder) || !Directory.Exists(_folder))
            {
                EditorUtility.DisplayDialog("Excel Tools", "请先选择 Excel 文件夹", "确定");
                return;
            }
            if (force) CExcelIncrementalGenerator.Clear();
            RefreshFileList();

            var options = new CExcelGenerateOptions
            {
                OutputFolder = _outputFolder,
                Namespace = _namespace,
                PrimaryKey = string.IsNullOrWhiteSpace(_primaryKey) ? null : _primaryKey,
                GenerateJson = _generateJson,
                GenerateClass = _generateClass,
                JsonResourcesFolder = _jsonResourcesFolder,
                ResourcesPath = _resourcesPath,
                EncryptJson = _encryptJson,
                StrictTypeCheck = _strictTypeCheck,
                ArraySeparators = _arraySeparators,
                SkipRowsWithoutKey = _skipRowsWithoutKey,
            };

            int generated = 0, skipped = 0, failed = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (FileStatus file in _files)
            {
                if (!CExcelIncrementalGenerator.IsChanged(file.Path))
                {
                    file.State = "skipped";
                    skipped++;
                    continue;
                }

                CExcelGenerateResult result = CExcelGenerator.GenerateAllSheets(file.Path, options);
                if (result.Success)
                {
                    file.State = "generated";
                    CExcelIncrementalGenerator.MarkGenerated(file.Path);
                    generated++;
                }
                else
                {
                    file.State = "failed";
                    file.Detail = string.Join("\n", result.Issues);
                    failed++;
                }
            }

            sw.Stop();
            AssetDatabase.Refresh();

            _summary = $"完成：新增 {generated} 个表，未变化跳过 {skipped} 个，失败 {failed} 个（耗时 {sw.ElapsedMilliseconds}ms）";
            if (failed > 0) _summary += "，详见列表错误提示";

            if (failed > 0)
                Debug.LogError("[CoffeeBean.Excel] 批量生成有失败:\n" + _summary);
            else
                Debug.Log("[CoffeeBean.Excel] " + _summary);
        }
    }
}
