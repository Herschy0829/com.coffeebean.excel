# Changelog

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
