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
    [CoffeeBeanTool("Excel 配置表工具", "文件夹批量生成 / 单表校验预览 / 增量生成（代码 + .cbcfg 数据 → 内嵌包，写在 Assets 之外）", "Excel")]
    public sealed class CExcelToolsWindow : EditorWindow
    {
        private const string PrefFolder = "CoffeeBean.Excel.Folder";
        private const string PrefCodeFolder = "CoffeeBean.Excel.CodeFolder";
        private const string PrefPackageName = "CoffeeBean.Excel.PackageName";
        private const string PrefNamespace = "CoffeeBean.Excel.Namespace";
        private const string PrefApiStyle = "CoffeeBean.Excel.ApiStyle";
        private const string PrefCompressData = "CoffeeBean.Excel.CompressData";
        private const string PrefEncryptData = "CoffeeBean.Excel.EncryptData";
        private const string PrefStrictTypeCheck = "CoffeeBean.Excel.StrictTypeCheck";
        private const string PrefArraySeparators = "CoffeeBean.Excel.ArraySeparators";
        private const string PrefSkipRowsWithoutKey = "CoffeeBean.Excel.SkipRowsWithoutKey";

        private string _folder;
        private string _codeFolder = "Packages/com.coffeebean.config.generated";
        private string _packageName = "com.coffeebean.config.generated";
        private string _namespace = "CoffeeBean";
        private CExcelApiStyle _apiStyle = CExcelApiStyle.Legacy;
        private string _primaryKey = string.Empty;
        private bool _generateData = true;
        private bool _generateClass = true;
        private bool _compressData = true;
        private bool _encryptData = true;
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
            _codeFolder = EditorPrefs.GetString(PrefCodeFolder, "Packages/com.coffeebean.config.generated");
            _packageName = EditorPrefs.GetString(PrefPackageName, "com.coffeebean.config.generated");
            _namespace = EditorPrefs.GetString(PrefNamespace, "CoffeeBean");
            _apiStyle = (CExcelApiStyle)EditorPrefs.GetInt(PrefApiStyle, (int)CExcelApiStyle.Legacy);
            _compressData = EditorPrefs.GetBool(PrefCompressData, true);
            _encryptData = EditorPrefs.GetBool(PrefEncryptData, true);
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
            if (GUILayout.Button("清理生成包", GUILayout.Height(28)))
                CleanGeneratedPackage();
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_summary))
                EditorGUILayout.HelpBox(_summary, MessageType.Info);

            // 生成选项
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("生成选项", EditorStyles.boldLabel);
            _codeFolder = EditorGUILayout.TextField("代码包目录（内嵌包）", _codeFolder);
            _packageName = EditorGUILayout.TextField("包名（= StreamingAssets 子目录）", _packageName);
            _namespace = EditorGUILayout.TextField("命名空间", _namespace);
            _apiStyle = (CExcelApiStyle)EditorGUILayout.EnumPopup("接口风格", _apiStyle);
            if (_apiStyle == CExcelApiStyle.Legacy)
                EditorGUILayout.LabelField(
                    "Legacy（对齐项目既有 *_DataGetter）：GetData / GetDataByID / GetDataNullID / GetDataByIndex / " +
                    "GetDataNullIndexNull / GetArray / GetArrayLenth / Get<字段>ProptyList，类放全局命名空间，业务代码无需改动。",
                    EditorStyles.wordWrappedMiniLabel);
            else
                EditorGUILayout.LabelField(
                    "Modern（本模块原有风格）：<表>Getter + Get / GetAll / GetByIndex / All / Find / FindAll，类放上面的命名空间。",
                    EditorStyles.wordWrappedMiniLabel);
            _primaryKey = EditorGUILayout.TextField("主键列（空 = 自动）", _primaryKey);
            EditorGUILayout.BeginHorizontal();
            _generateData = EditorGUILayout.Toggle("生成数据", _generateData);
            _generateClass = EditorGUILayout.Toggle("生成 C# 类 + Getter", _generateClass);
            _compressData = EditorGUILayout.Toggle("压缩数据", _compressData);
            _encryptData = EditorGUILayout.Toggle("加密数据", _encryptData);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                "产物布局：<包目录>/<表>/Code/*.cs 与 <包目录>/<表>/Data/<表>" + CExcelDataContainer.Extension +
                "（都在 Assets 之外；打包时由构建钩子把各 Data 目录挂进 StreamingAssets）。",
                EditorStyles.wordWrappedMiniLabel);
            if (!string.Equals(System.IO.Path.GetFileName(_codeFolder), _packageName, System.StringComparison.Ordinal))
                EditorGUILayout.HelpBox("包名与代码包目录的末级目录名不一致 —— 构建钩子与运行时会错位，请保持一致。", MessageType.Warning);

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
                EditorPrefs.SetString(PrefCodeFolder, _codeFolder);
                EditorPrefs.SetString(PrefPackageName, _packageName);
                EditorPrefs.SetString(PrefNamespace, _namespace);
                EditorPrefs.SetInt(PrefApiStyle, (int)_apiStyle);
                EditorPrefs.SetBool(PrefCompressData, _compressData);
                EditorPrefs.SetBool(PrefEncryptData, _encryptData);
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

        /// <summary>
        /// 删除整个生成包目录。
        ///
        /// **为什么需要它**：生成器只写自己的产物，**不清理上一版留下的文件**。产物命名/布局一旦变化
        /// （例如 0.6.0 把 `前缀_1Config` 改成 `前缀Chapter1`），旧文件会与新文件共存并引用已不存在的类型，
        /// 直接编译不过。这时正确的做法是"清包 + 重新生成"，而不是手工去 Assets 里挑文件。
        ///
        /// 会一并删除该目录里的手工定制（如自定义 asmdef / package.json），所以必须二次确认。
        /// </summary>
        private void CleanGeneratedPackage()
        {
            if (string.IsNullOrEmpty(_codeFolder))
            {
                EditorUtility.DisplayDialog("清理生成包", "代码包目录为空", "确定");
                return;
            }

            string full = System.IO.Path.GetFullPath(_codeFolder);
            bool exists = Directory.Exists(full);
            if (!EditorUtility.DisplayDialog("清理生成包",
                    (exists ? "将删除整个生成包目录：\n" + full : "该目录不存在：\n" + full) +
                    "\n\n注意：放在该目录里的手工定制（自定义 asmdef / package.json 等）也会一并删除。" +
                    "\n删完记得重新生成一次。", "删除", "取消"))
                return;

            if (exists)
            {
                Directory.Delete(full, true);
                AssetDatabase.Refresh();
                _summary = "已清理生成包：" + full;
            }
            else
            {
                _summary = "生成包目录不存在：" + full;
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
                CodeFolder = _codeFolder,
                PackageName = _packageName,
                Namespace = _namespace,
                ApiStyle = _apiStyle,
                PrimaryKey = string.IsNullOrWhiteSpace(_primaryKey) ? null : _primaryKey,
                GenerateData = _generateData,
                GenerateClass = _generateClass,
                CompressData = _compressData,
                EncryptData = _encryptData,
                StrictTypeCheck = _strictTypeCheck,
                ArraySeparators = _arraySeparators,
                SkipRowsWithoutKey = _skipRowsWithoutKey,
            };

            // 包名非法直接拦下：UPM 解析失败会让整个工程打不开（实测），别等生成完才发现
            string packageNameError = CExcelGenerator.ValidatePackageName(_packageName);
            if (packageNameError != null)
            {
                EditorUtility.DisplayDialog("包名非法",
                    "包名 \"" + _packageName + "\" 不合法：" + packageNameError +
                    "\n\n非法包名会让 UPM 解析失败、整个工程都打不开，已中止生成。", "确定");
                return;
            }

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
