# Changelog

## [0.2.1] - 2026-08-28

### Changed
- **工具入口收敛到 CoffeeBean Hub**：Excel 工具窗口加 CoffeeBeanToolAttribute 标记（模块内复制同名定义，无需依赖 core），由 Window > CoffeeBean 统一发现打开；移除独立菜单项

# Changelog

## [0.2.0] - 2025-xx-xx

### Changed
- **统一命名空间**：全部类型迁移到 `CoffeeBean` 根命名空间（业务只需 `using CoffeeBean;` 即可使用所有模块主类型），模块内部辅助 / 测试 / 示例保留 `CoffeeBean.X` 子命名空间（父命名空间自动可见）
- **破坏性变更**：旧 `using CoffeeBean.X;` 需移除（类型已上移到根命名空间）

# Changelog

## [0.1.5] - 2025-xx-xx

### Fixed / Notes
- **多语言表加密确认无乱码**：加密是纯字节级 XOR（UTF8 字节 → XOR → TextAsset.bytes 原样还原），
  与字符编码无关，中文 / 日文 / emoji 均无损往返——**无需 Language 表例外**（区别于 Idle 项目的字符级加密）
- 新增多语言专项测试 2 个（编解码往返 + 生成加密表完整链路还原）

## [0.1.4] - 2025-xx-xx

### Added
- **配置 JSON 混淆加密**（对齐 Idle 项目的 GetSimpleEncyptString 做法）：生成选项 `EncryptJson`（默认 true），
  生成的 JSON 写 XOR 密文字节（`CExcelCrypto`，确定性 key 流），打包产物里配置不再是明文
- Getter 模板内嵌解密逻辑（`Decode(asset.bytes)`），运行时透明解密，业务无感知
- 窗口新增"加密 JSON"开关（EditorPrefs 记忆；调试时可关闭直接查看 JSON）

### Notes
- **安全边界**：这是混淆级保护（key 硬编码在生成代码里，防普通读取 / 防小白，不防专业逆向）；
  真正的安全需服务器下发配置 / AssetBundle 加密 / 代码混淆。客户端资源永远无法真正防提取。
- 关闭加密会改变既有密文产物的读取方式（Getter 模板随之不含解密），重新生成即可

## [0.1.3] - 2025-xx-xx

### Fixed
- **运行时加载缺陷**：JSON 不再生成到代码输出目录（非 Resources 下 `Resources.Load` 读不到），
  新增 `JsonResourcesFolder`（默认 `Assets/Resources/Configs`）与 `ResourcesPath`（默认 `Configs`）——
  JSON 直接生成进 Resources 子目录，Getter 的 `AssetPath` 与 Resources 相对路径对齐，生成完立即可用
- 窗口生成选项新增"JSON Resources 目录 / Resources 相对路径"配置（EditorPrefs 记忆）

### Notes
- 运行时加载方式：`Resources.Load<TextAsset>(ResourcesPath + "/" + 类名)` → JsonUtility 反序列化
  （`{"data":[...]}` 包装）；需要 Addressables / AssetBundle 按需加载的场景请自定义加载或后续版本支持

## [0.1.2] - 2025-xx-xx

### Changed
- **工具窗口重构**：主窗口改为**文件夹批量生成**（选择一次文件夹后 EditorPrefs 记忆，无需重选），
  一键增量生成 / 强制重新生成 / 清空状态，逐表状态列表（已生成 / 未变化跳过 / 失败）
- **二级窗口 `CExcelFileWindow`**：单文件 sheet 选择 → 预览（表头 / 列类型 / 行数 / 列说明 / 问题）→ 校验 / 单 sheet 生成
- **增量生成 `CExcelIncrementalGenerator`**：按文件最后修改时间记录生成状态（EditorPrefs 持久），
  未变化的表跳过重新生成（对齐 Idle 的 IsFileChange 机制）

### Added
- 增量状态测试 6 个（修改时间对比 / 记录 / 单文件与全量清空）

## [0.1.1] - 2025-xx-xx

### Added
- **多 Sheet 支持**：`CExcelReader.GetSheetNames`；`CExcelGenerator.GenerateAllSheets` 全 sheet 生成
  （跳过名字含 `sheet`/`debug` 的 sheet，对齐 Idle 约定）
- **分章节（多章节）**：sheet 名 `前缀_数字`（如 `ChapterConfig_1`）→ 生成
  章节基类 `前缀ConfigBase` + 每章节子类 + 每章节 Getter + **聚合 Getter `前缀Getter`**
  （`GetByID(id, chapterId)` / `GetChapter(chapterId)` 按章节查询，对齐 Idle 的 DataGetter 模式）
- **列中文说明**：双行表头时表头行上方的中文说明行作为生成代码的字段注释（对齐 Idle）；
  单行表头（英文列名）生成代码为纯英文（文件头/注释均英文化，去掉工具性中文）

### Changed
- `Generate` 支持 `SheetName` / `ClassName` 选项；`GenerateFolder` 走全 sheet 生成
- 生成代码注释：字段注释取表头说明行（无则用源列名）

## [0.1.0] - 2025-xx-xx

### Added
- **`CExcelReader` 读取层**（MiniExcel，Editor-only）：表头自动检测（带类型后缀列名最多行，兼容双行表头）、
  列别名映射、空行/注释行跳过、错误/警告分级问题列表
- **`CExcelTypeInfer` 类型推断**：列名后缀表（`_i/_l/_f/_d/_b/_s` + 数组 `_ia/_la/...`）、无后缀按值推断、
  字段名转换（去后缀 + PascalCase）、数组分隔符解析
- **`CExcelGenerator` 生成层**：一张表生成三件套——
  JSON 数据（`{"data":[...]}`，运行时 Resources 加载）+ C# 强类型数据类 + Getter 加载器（主键查询）；
  批量生成目录（跳过 `~$` 临时文件）
- **`CExcelToolsWindow` 编辑器窗口**：选表 → 预览（表头/列类型/行数/问题）→ 生成 / 批量生成
- **ExcelDemo 示例**：一键生成演示配置表（双行表头样例）并跑通 读取 → 生成 全流程
- EditMode 测试 25 个：读取 / 推断 / 生成（含 JSON 合法性断言）
