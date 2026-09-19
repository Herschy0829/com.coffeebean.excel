# CoffeeBean Excel（com.coffeebean.excel）

CoffeeBean 框架的 **Excel 配置表工具模块**（Editor-only）：读取 → 列类型 → 生成 **内嵌包产物**（`<表>/Code/` 代码 + `<表>/Data/<表>.cbcfg` 数据容器，都在 Assets 之外）。

> 设计文档：`docs/design-excel.md`（v0.5.0）
> ⚠ 升级到 **0.5.0** 前请先看 `CHANGELOG.md` 顶部的破坏性变更：`_b` 现在是 **BigInteger**（bool 改用 `_bool`），
> 生成的 Getter 改用 **Newtonsoft.Json** 反序列化。

## 安装

```json
{
  "dependencies": {
    "com.coffeebean.excel": "https://github.com/Herschy0829/com.coffeebean.excel.git#v0.5.0"
  }
}
```

会自动带上 `com.unity.nuget.newtonsoft-json`（生成的 Getter 需要它来读 BigInteger / decimal / 时间 / Guid / 字典 / Rect）。

## 快速使用

### 1. 表结构约定（列名带类型后缀）

数组后缀 = 标量后缀 + `a`（`_i` → `_ia`）。完整 27 种见下表，或在 Unity 里看
`Window > CoffeeBean > Excel · 类型映射说明`（那张表由代码渲染，不会和实现不一致）。

| 分组 | 后缀 → C# 类型 |
|------|----------------|
| 整数 | `_i` int、`_l` long、**`_b` BigInteger**、`_by` byte、`_sb` sbyte、`_sh` short、`_us` ushort、`_u` uint、`_ul` ulong |
| 小数 | `_f` float、`_d` double、**`_dec` decimal** |
| 布尔 | `_bool` bool |
| 文本 | `_s` string、`_c` char |
| 时间/标识 | `_time` DateTime、`_span` TimeSpan、`_guid` Guid |
| 枚举 | `_e`（表内生成）、`_e:类型`（引用已编译枚举）、`_flags` / `_flags:类型` |
| 结构 | `_v2`/`_v3`/`_v4`、`_quat`、`_color`、`_rect` |
| 复合 | `_kv` `Dictionary<string,string>` |

**无后缀列**按值保守推断：全整数 → `int`；超 int32 → `long`；整数但超 long → **`string`**（不猜 double）；
含小数/指数 → `double`；整列 `true/false` → `bool`；否则 `string`。
⚠ 纯 `1/0` 的列会算 **int**（int 优先），想当 bool 就写 `_bool`。

**数组分隔符**：默认 `;` `,` `|` `_`（含中文全角）—— **含 `_` 是因为真实项目里最常见的就是
`13_100`（id_数量）、`0.2_0.8_1`、`18_5_1_300_11_1_13_100`**，字符串数组也这么写（`6006_1`）。
可在生成选项里改（`CExcelGenerateOptions.ArraySeparators`）。
**例外**：向量/四元数/颜色/矩形只能用 `;` 或 `|`（元素内部是逗号）；字典数组 `_kva` 用 `|`；
**枚举数组永不按 `_` 拆**（`_` 是"名字_值"语法，`green_3` 不能被拆开）；
`[Flags]` 数组的 `|` 留给元素内部的位组合（`Cold|Hot;Cold` = 2 个元素）。

```xlsx
| Id_i | Name_s   | Price_f | Rewards_ia  | Enabled_bool | Gold_b               | State_e |
| 1    | 新手礼包 | 6.5     | 100_200_300 | true         | 12345678901234567890 | green   |
| 2    | 月卡     | 30      | 500         | false        | 999                  | idle    |
```

> **说明行 / 图例行**：真实表头下面常常还有一段"字段 | 说明"的图例，或者某列旁边贴一串临时算的数。
> 这些行主键是空的，会被**自动跳过并出警告（列出行号）**；跳完一行不剩则报错（多半是表头检测选错行）。
> 开关：`CExcelGenerateOptions.SkipRowsWithoutKey`（默认 true）。

> `_b`（BigInteger）列请把单元格设成**文本格式**：Excel 数字格式只保留 15 位有效数字，
> 超了会变科学计数法 —— 生成时会**直接报错**并提示，不会静默写个错的数。

### 2. 枚举（`_e` / `_e:类型`）

```xlsx
| Id_i | State_e    | Tags_flags |
| 1    | green      | Fire|Ice   |
| 2    | idle       | Poison     |
| 3    | boss_7     | Fire       |
```

- **自动编号**：不带值的取值从 0 起按首次出现顺序编号；`boss_7` = 显式值 7
  （按**最后一个下划线**拆，所以 `fire_dragon` 是完整名字）
- **生成的类型名** = 表名 + 字段名（`Building` 表的 `State_e` → `BuildingState`），生成在该表的数据类文件里
- **成员名**自动 PascalCase（`green_leaf` → `GreenLeaf`），改名会给警告
- **引用模式** `State_e:MyEnum` 不生成枚举，改用工程里**已编译**的枚举；生成时按实际枚举校验类型和成员，
  找不到会报错并**列出可用成员**
- **JSON 里存数字**（成员值），所以不需要任何 JSON 转换器
- 校验（错误级、阻塞生成）：**同一枚举两个成员同值**、同名被赋两个值、取值取不出合法成员名、
  引用类型/成员找不到、枚举列一个取值都没有、**跨表同名枚举成员不一致**

### 3. 生成

```csharp
using CoffeeBean;   // 生成类默认也在 CoffeeBean 命名空间

var options = new CExcelGenerateOptions
{
    // 代码与数据都生成到内嵌包（Assets 之外）；一表一文件夹：<表>/Code/ + <表>/Data/
    CodeFolder = "Packages/com.coffeebean.config.generated",
    PackageName = "com.coffeebean.config.generated",   // 必须与 CodeFolder 末级目录名一致
    StrictTypeCheck = true,   // 默认 true：每格按声明类型真解析，填错报第几行第几列
};
CExcelGenerateResult result = CExcelGenerator.Generate("Assets/Excel/ChapterConfig.xlsx", options);
// 或全 sheet / 整个目录：
CExcelGenerator.GenerateAllSheets(path, options);
CExcelGenerator.GenerateFolder(folder, options);
// 只校验不生成（预览窗口的"校验"按钮走这条）：
List<CExcelIssue> issues = CExcelGenerator.Validate(path, sheetName, options);
```

产物（全部在 `CodeFolder` 这个内嵌包下，**不写进 Assets**）：

- `<表>/Code/表名.cs` —— 强类型数据类 + 该表用到的枚举定义
- `<表>/Code/表名Getter.cs` —— 加载器（`All` 懒加载 + `Get(主键)` + 自注册供预加载）
- `<表>/Data/表名.cbcfg` —— 表数据容器（Deflate 压缩 + XOR 加密，见 §6）
- `{Namespace}.Generated.asmdef` —— 生成代码独立程序集（改表只重编译这一小个程序集）
- `Runtime/ConfigTableRuntime.cs` —— 运行时支撑（容器解码 + 预加载 + 数据路径解析）
- `package.json` + `coffeebean.configgen.json` —— 内嵌包清单与构建钩子标记（后者别删）

### 4. 接口风格：Legacy（**默认**）与 Modern

`CExcelGenerateOptions.ApiStyle` 决定生成的类名 / 文件名 / 成员名：

| | **Legacy（默认，对齐项目既有）** | Modern（本模块早期风格） |
|---|---|---|
| 文件 | `<表>_DataGetter.cs`、`<表>_Data.cs`、章节 `<前缀>_<N>_Data.cs` | `<表>.cs`、`<表>Getter.cs`、章节 `<前缀>Base/Chapter<N>` |
| 类 | `<表>_DataGetter` / `<表>_PropertyBase` / `<表>_DataBase` | `<表>Getter` / `<表>` |
| 取整表 | `GetData()` → `_DataBase`（`.DataArray` / `.ArrayLength`） | `All` |
| 按主键 | `GetDataByID(id)`（找不到：`LogError` + `id<=0` 给首行 / 否则给末行）、`GetDataNullID(id)`（找不到 null） | `Get(key)`（找不到 null）/ `GetAll(key)` |
| 按下标 | `GetDataByIndex(i)` / `GetDataNullIndexNull(i)` | `GetByIndex(i)` |
| 行数 | `GetArrayLenth()`（**沿用项目的拼写**） | `Count` |
| 数组 | `GetArray()` | `All` |
| 每字段列表 | `Get<字段>ProptyList()` | — |
| 重复 ID | `GetDataBySameID(id, lev)` / `GetDataBySameIDMaxlev(id)` | `GetAll(key)` |
| 命名空间 | **全局命名空间**（业务代码不用加 using） | `options.Namespace` |
| 字段名 | **原样列名**（`mode_i` → `mode`、`Des_s` → `Des`） | PascalCase（`Mode`/`Des`） |

**为什么默认 Legacy**：真实工程里已有 55 个 `*_DataGetter.cs`、139 处调用点，只有名字逐字一致（连
`GetArrayLenth` 这个笔误、字段名大小写都一致）生成产物才能**直接替换**老代码、业务代码一行不改。
Modern 保留给新项目/新表。

**章节表的"当前章节"是注入的（方案 B）**：生成包是独立程序集，引用不到游戏业务代码（Assembly-CSharp），
所以生成代码绝不出现 `PlayerDataMgr` 这类游戏类型；由游戏在启动早期实现接口并注入一次：

```csharp
public class GameConfigContext : IConfigContext
{
    public int CurrentChapterId => PlayerDataMgr._Ins?.data?.moduleRequest?.curChapterID ?? 1;
}
ConfigTableRuntime.Context = new GameConfigContext();   // 启动早期
```

之后章节表所有接口的 `chapterID` 都可以省略（`-1` = 当前章节）：

```csharp
ChapterConfig_DataGetter.GetDataByID(100);              // 当前章节
ChapterConfig_DataGetter.GetDataByID(100, 2);           // 指定第 2 章
ChapterConfig_DataGetter.GetData();                     // 当前章节整表
```

没注入时回退到 `ConfigTableRuntime.FallbackChapterId`（默认 1，与项目里 `?? 1` 的既有行为一致）并**只警告一次**；
章节号没配置时 `LogWarning("策划没有配置 第N章节 ... 默认给上一章节数据")` 并返回最后一章数据（照抄项目行为）。

### 4.1 生成时的两个行为（踩过坑，别当 bug）

- **主键自动选择优先 ID 命名**：打分 = 名字为 `ID`（+100）/ 以 `ID` 开头（+60）/ 非字符串（+2）/
  每行都有合法值（+30）。为什么：老规则"第一个每行都有值的列"会把备注列 `Bz_s` 选成主键
  （实测 `BuildingConfig`、`ChapterConfig` 都中过），主键一换 `GetDataBySameID` 会**静默给错数据**。
  主键列上空的/非法的行会被**跳过并警告列出行号**（说明行、图例、草稿行）。
- **竖排键值对表会被跳过**：表头行只有 1 个带类型后缀列名的表（如项目的 `常量表.xlsx`，一行一个常量）
  不是常规配置表，生成时给 Warning 并跳过（它由项目自己的 `AppConstGenerator` 维护），
  不再报一堆"int 列填的值不合法"的假错误。

### 5. 多 Sheet 与分章节（对齐项目约定）

- **多 Sheet**：`GenerateAllSheets` 处理全部 sheet，**跳过名字含 `sheet`/`debug` 的**（如默认 `Sheet1`）
- **分章节**：sheet 名形如 `前缀_数字`（`ChapterConfig_1`、`ChapterConfig_2`）→ 同前缀聚合：

```
ChapterConfig/
  Code/  ChapterConfigBase.cs                     章节基类（全字段 + 共用枚举定义）
         ChapterConfigChapter1.cs / Chapter2.cs   每章节子类（: 基类）
         ChapterConfigChapter1Getter.cs / ...     每章节独立加载器
         ChapterConfigGetter.cs                   聚合加载器（按章节查询）
  Data/  ChapterConfig_1.cbcfg / _2.cbcfg         每章节数据容器
```

各章节的枚举取值会**取并集后统一编号**（同一成员在所有章节里拿到同一个值）。

```csharp
ChapterConfigGetter.Get(100, chapterId: 1);       // 按章节 + 主键查询
ChapterConfigGetter.GetChapter(chapterId: 2);     // 取某章节全部行（基类 IReadOnlyList）
ChapterConfigGetter.Chapter1;                     // 第一章强类型 IReadOnlyList
```

### 5. 列中文说明（生成代码注释）

双行表头时（第 0 行中文说明 + 第 1 行字段名），中文说明行会作为**字段注释**写入生成的 C# 类；
单行表头（英文列名）时生成代码为纯英文（含生成出来的枚举定义，注释一律英文）。

### 6. 运行时读取

```csharp
using CoffeeBean;  // 生成类在 CoffeeBean 命名空间（默认）

// 启动流程里跑一次预加载协程；之后所有同步 API 直接可用
yield return ConfigTableRuntime.PreloadAll();

// —— 普通表（表名 = ChapterConfig）——
ChapterConfigGetter.All;                        // IReadOnlyList<ChapterConfig>（懒加载，只读）
ChapterConfigGetter.Count;                      // 行数
ChapterConfigGetter.IsLoaded;                   // 是否已加载
ChapterConfigGetter.Get(1);                     // 按主键取一行；同一个键有多行时给**第一行**；找不到 null
ChapterConfigGetter.GetAll(1);                  // 该主键的**全部**行（IReadOnlyList；没有则空列表）
ChapterConfigGetter.TryGet(1, out var one);     // 同上，但返回 bool（不抛异常）
ChapterConfigGetter.Contains(1);                // 主键是否存在
ChapterConfigGetter.GetByIndex(0);              // 按下标取（越界 null）
ChapterConfigGetter.Find(x => x.SwitchLevelId > 0);      // 第一个满足条件；没有则 null
ChapterConfigGetter.FindAll(x => x.SwitchLevelId > 0);   // 所有满足条件（永不为 null）
ChapterConfigGetter.Reload();                   // 丢缓存，下次访问重读（热更后用）
ChapterConfigGetter.LoadFrom(containerBytes);   // 直接灌容器字节（测试/自定义加载）

// —— 章节表（sheet: ChapterConfig_1 / ChapterConfig_2）——
ChapterConfigGetter.Chapters;                   // int[]：可用章节号
ChapterConfigGetter.ChapterCount;               // 章节数
ChapterConfigGetter.HasChapter(2);              // 该章节是否存在
ChapterConfigGetter.GetChapter(2);              // IReadOnlyList<ChapterConfigBase>（未知章节=空数组）
ChapterConfigGetter.Get(100, chapterId: 1);     // 章节 + 主键（同键多行时给首行）
ChapterConfigGetter.GetAll(100, chapterId: 1);  // 章节 + 主键的全部行
ChapterConfigGetter.TryGet(100, 1, out var row);
ChapterConfigGetter.Contains(100, 1);
ChapterConfigGetter.Chapter1;                   // IReadOnlyList<ChapterConfigChapter1>（强类型）
ChapterConfigGetter.Reload();                   // 清所有章节缓存
```

**表里没有可做键的列时（没有 `*_i`/`*_l`/`*_s`/`*_e` 这类列）**：照样生成，只是**不产出按主键的接口**
（`Get`/`GetAll`/`TryGet`/`Contains`），改用非主键访问——`All` / `Count` / `GetByIndex(i)` / `Find(pred)` / `FindAll(pred)`；
生成时会给出对应警告，生成的代码里也有注释说明。章节族同理（只有 `GetChapter`/`ChapterN`/`HasChapter`）。

> 需要"两个字段都相同才算同一行"（如 游戏里 `GetDataBySameID(id, lev)`）时，用
> `Getter.FindAll(x => x.Id == id && x.Lev == lev)`（我们只认单一主键，不猜复合键）。

**加载机制**：数据是 `<表>/Data/<表>.cbcfg` 容器 → `ConfigTableRuntime` 解容器（解 XOR → 解 Deflate → 校验长度与校验和）→ **Newtonsoft.Json** 反序列化 `{"data":[...]}`。

- **Editor** 直接读内嵌包本体（`Packages/<包名>/...`）；**Player** 读 `Application.streamingAssetsPath/<包名>/...`。
- 数据能进打包产物，靠的是本模块的**构建钩子**（`BuildPlayerProcessor.PrepareForBuild` + `AddAdditionalPathToStreamingAssets`）把每个 `<表>/Data` 挂进去 —— 包内的 `StreamingAssets/` 文件夹**不会被 Unity 自动收录**（官方要求 StreamingAssets 必须在 `Assets/` 根下）。
- Android / Web：`streamingAssetsPath` 是 URL，`File.Read` 读不到 → **必须用 `PreloadAll()`**（内部走 UnityWebRequest）。

**压缩 + 加密（默认都开）**：`CompressData`（Deflate）/ `EncryptData`（XOR 混淆级）。顺序恒为**先压缩再加密** —— 反过来密文近似随机、压不动。
> ⚠ 加密是**混淆级**保护（key 硬编码在生成代码里，不防专业逆向）。
> 两个开关只影响数据文件的**头部 flags**，**不影响生成的代码**；想直接看明文调试就把两个开关都关掉重新生成（容器头 16 字节之后就是明文 JSON）。

> 需要 Addressables / AssetBundle 按需加载的大表：当前 Getter 固定**一次性读入内存**
> （数据在包内 `<表>/Data`，打包时由构建钩子挂进 StreamingAssets）；要按需加载需要自己接 Addressables。

### 7. 编辑器窗口

| 窗口 | 入口 | 用途 |
|------|------|------|
| Excel 配置表工具 | `Window > CoffeeBean` → `Excel · Excel 配置表工具` | 文件夹批量增量生成、生成选项、严格校验开关、后端状态 |
| 类型映射说明 | `Window > CoffeeBean` → `Excel · 类型映射说明` | **活文档**：后缀对照表 + 枚举语法 + 规则 + 规划中类型（可整表复制成纯文本） |
| 单文件校验/预览 | 主窗口列表行「预览/校验」 | 选 sheet → 看表头/列类型/问题 → 真校验 / 生成此 sheet |

**增量生成**：状态 = **模板版本 + 文件修改时间**。升级框架（模板版本变了）后所有表会自动重新生成一次 ——
只比修改时间的话，"表没改"会一直跳过，产物永远停在旧模板。

### 8. 读取 API（供工具链/其他模块复用）

```csharp
CExcelReadResult read = CExcelReader.Read(path, new CExcelReadOptions
{
    ColumnAliases = new Dictionary<string, string[]> { ["Id_i"] = new[] { "Id_i", "商品ID" } },
});
// read.Columns / read.Rows（规范列名 → 原始值）/ read.Issues（错误/警告分级）
```

## 约束与约定

- **Editor-only**：运行时不直接读 xlsx，只读生成的 JSON
- 表头自动检测：前 3 行中带类型后缀列名最多的行（兼容"中文说明行 + 字段名行"双行表头）
- 空行与注释行（首列 `#`）自动跳过；缺失文件 / 空表 / 无类型后缀列 = 阻塞错误
- 主键列自动选择第一个可做键的列（整数族 / string / char / Guid；**数组列不算**），可在选项里指定
- 空单元格 → 确定性默认值（数值 0、字符串空串、数组空数组、枚举 0、时间零值、结构全 0）
- 生成代码用 Newtonsoft 反序列化：`package.json` 已声明依赖，普通 asmdef 会自动引用该插件 DLL

## 目录结构

```
Editor/
├── CoffeeBean.Excel.Editor.asmdef
├── Core/        CExcelReader / CExcelValue / CExcelModels / CExcelSheetName / CExcelTestFactory
│                CExcelJsonBackend（Newtonsoft 反射访问层）/ CExcelJsonProbe（测试探针）
├── Infer/       CExcelTypeInfer(CExcelFieldKind) / CExcelTypeCatalog（唯一类型表）/ CExcelEnumDef
├── Generate/    CExcelGenerator / CExcelCellJson（手写 JSON 字面量）/ CExcelTableValidator
│                CExcelIncrementalGenerator / CExcelCrypto
├── Window/      CExcelToolsWindow / CExcelFileWindow / CExcelTypeMappingWindow
└── Plugins/     MiniExcel（Editor-only）
Runtime/Bridge/  CoffeeBean.Excel.Bridge.asmdef（仅在装了 core 时编译：注册进 CoffeeBean Hub）
```

## 测试

EditMode 测试 **124 条**（全绿）：

- 读取：表头检测 / 别名 / 跳过 / 错误分级
- 类型表：每个 kind 恰好一条、后缀互不结尾、数组块偏移、示例真能生成合法 JSON
- **后端能力**：逐类型真反序列化（示例 → JSON → 真实 `JsonConvert.DeserializeObject`）+
  锁住"JsonUtility 读不回哪些"、BigInteger 科学计数法有损、颜色三种写法一致、四元数 4 数 vs 欧拉角、数组分隔符
- 枚举：自动编号 / 显式值 / 拆分规则 / 成员名规范化 + 全部校验分支 + 引用模式 + 跨表重名 + 章节并集
- 校验：坏值必须报行号列名、`StrictTypeCheck=false` 降级、空值不算错
- 生成：JSON 可被 Newtonsoft 读回、C# 类/Getter/枚举文本、多章节产物、模板版本标记

## 版本约定

- SemVer + git tag `vX.Y.Z`；每个版本对应 GitHub Release（CHANGELOG 派生说明）
