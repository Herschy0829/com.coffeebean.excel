# CoffeeBean Excel（com.coffeebean.excel）

CoffeeBean 框架的 **Excel 配置表工具模块**（Editor-only）：读取 → 类型推断 → 生成 **JSON + C# 数据类 + Getter** 三件套。

独立模块（无依赖），供游戏工程与 purchase 等模块使用。

> 设计文档：`docs/design-excel.md`（v0.1）

## 安装

```json
{
  "dependencies": {
    "com.coffeebean.excel": "https://github.com/Herschy0829/com.coffeebean.excel.git#v0.1.0"
  }
}
```

## 快速使用

### 1. 表结构约定（列名带类型后缀）

| 后缀 | 类型 | 后缀 | 类型 |
|------|------|------|------|
| `_i` | int | `_ia` | int[] |
| `_l` | long | `_la` | long[] |
| `_f` | float | `_fa` | float[] |
| `_d` | double | `_da` | double[] |
| `_b` | bool | `_ba` | bool[] |
| `_s` | string | `_sa` | string[] |

无后缀列按值自动推断（全整数→int，含小数→double，true/false/1/0→bool，否则 string）。
数组分隔符：`;`（含中文 `；`）或 `,`。

```xlsx
| Id_i | Name_s | Price_f | Rewards_ia | Enabled_b |
| 1    | 新手礼包 | 6.5   | 100;200;300 | 1         |
```

### 2. 生成三件套

```csharp
using CoffeeBean.Excel;

var options = new CExcelGenerateOptions
{
    OutputFolder = "Assets/Configs/Generated",
    Namespace = "Config",
};
CExcelGenerateResult result = CExcelGenerator.Generate("Assets/Excel/ChapterConfig.xlsx", options);
// 或批量（全 sheet）：
CExcelGenerateResult all = CExcelGenerator.GenerateAllSheets("Assets/Excel/ChapterConfig.xlsx", options);
```

产物（输出目录下）：
- `ChapterConfig.json` —— 表数据（`{"data":[...]}`，运行时 Resources 加载）
- `ChapterConfig.cs` —— 强类型数据类（`Id` / `Name` / `Price` / `Rewards`）
- `ChapterConfigGetter.cs` —— 加载器

### 3. 多 Sheet 与分章节（对齐项目约定）

- **多 Sheet**：`GenerateAllSheets` 处理全部 sheet，**跳过名字含 `sheet`/`debug` 的**（如默认 `Sheet1`）
- **分章节**：sheet 名形如 `前缀_数字`（`ChapterConfig_1`、`ChapterConfig_2`）→ 同前缀聚合：

```
ChapterConfig_1.json / ChapterConfig_2.json    每章节数据
ChapterConfigConfigBase.cs                     章节基类（全字段）
ChapterConfig_1Config.cs / _2Config.cs         每章节子类（: 基类）
ChapterConfig_1Getter.cs / _2Getter.cs         每章节独立加载器
ChapterConfigGetter.cs                         聚合加载器（按章节查询）
```

```csharp
ChapterConfigGetter.GetByID(100, chapterId: 1);   // 按章节 + 主键查询
ChapterConfigGetter.GetChapter(chapterId: 2);     // 取某章节全部行（基类 IEnumerable）
ChapterConfigGetter.Chapter1;                     // 第一章强类型 List
```

### 4. 列中文说明（生成代码注释）

双行表头时（第 0 行中文说明 + 第 1 行字段名），中文说明行会作为**字段注释**写入生成的 C# 类；
单行表头（英文列名）时生成代码为纯英文。

### 5. 运行时读取

```csharp
// JSON 生成在 Resources/Configs/ 下（生成选项 JsonResourcesFolder，默认 Assets/Resources/Configs）
var all = ChapterConfigGetter.All;       // List<ChapterConfig>（懒加载）
var cfg = ChapterConfigGetter.Get(1);    // 按主键查询（默认第一个 *_i/_l/_s 列）
```

**运行时加载机制**：生成的 Getter 用 `Resources.Load<TextAsset>("Configs/ChapterConfig")`（`ResourcesPath` + 类名）
→ JsonUtility 反序列化 `{"data":[...]}` 包装。**JSON 必须生成在 Resources 目录下**（生成器默认直接写入
`Assets/Resources/Configs/`，与 Getter 的 AssetPath 对齐，无需手动拷贝）。

**JSON 加密（默认开启）**：生成选项 `EncryptJson = true` 时，JSON 以 XOR 密文字节写入（`CExcelCrypto`，
对齐 Idle 项目的简单加密），Getter 运行时透明解密。打包产物里的配置不是明文，防普通读取。
> ⚠️ 这是**混淆级**保护（key 在生成代码里，不防专业逆向）；真正安全需服务器下发 / AB 加密 / 代码混淆。
> 调试时可在窗口关闭"加密 JSON"重新生成以便直接查看。

> 需要 Addressables / AssetBundle 按需加载的大表场景：当前 Getter 固定走 Resources（打进包）；
> 可自定义加载器或等待后续版本支持 LoadMode。

### 6. 编辑器窗口

`Window > CoffeeBean > Excel Tools`：选表 → 预览（表头行/列类型/行数/问题）→ 生成 / 批量生成目录。

### 7. 读取 API（供工具链/其他模块复用）

```csharp
CExcelReadResult read = CExcelReader.Read(path, new CExcelReadOptions
{
    ColumnAliases = new Dictionary<string, string[]> { ["Id_i"] = new[] { "Id_i", "商品ID" } },
});
// read.Columns / read.Rows（规范列名 → 原始值）/ read.Issues（错误/警告分级）
```

## 约束与约定

- **Editor-only**：运行时不直接读 xlsx（打包体积 / 平台限制），运行时只读生成的 JSON
- 表头自动检测：前 3 行中带类型后缀列名最多的行（兼容"中文说明行 + 字段名行"双行表头）
- 空行与注释行（首列 `#`）自动跳过；缺失文件 / 空表 / 无类型后缀列 = 阻塞错误
- 主键列自动选择第一个 `*_i` / `*_l` / `*_s` 列，可在选项里指定

## 目录结构

```
Editor/
├── CoffeeBean.Excel.Editor.asmdef
├── Core/        CExcelReader / CExcelValue / 模型（问题分级）
├── Infer/       CExcelTypeInfer（后缀表 / 无后缀推断 / 字段名）
├── Generate/    CExcelGenerator（JSON + C# 类 + Getter）
├── Window/      CExcelToolsWindow
└── Plugins/     MiniExcel（Editor-only）
```

## 测试

EditMode 测试 25 个：读取（表头检测/别名/跳过/错误分级）/ 类型推断（后缀全组合/兜底/数组）/ 生成（产物内容与 JSON 合法性断言）。

## 版本约定

- SemVer + git tag `vX.Y.Z`；每个版本对应 GitHub Release（CHANGELOG 派生说明）
