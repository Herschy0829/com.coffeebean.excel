# Changelog

## [0.5.0] - 2026-09-18

### ⚠ 破坏性变更（升级必读）

1. **`_b` 从 bool 改成 `BigInteger`**（大整数），布尔改用 **`_bool` / `_boola`**。
   - 老表里 `Enabled_b` 填 `true/false` 的地方现在会报"不是整数"，并且会额外给一条**迁移警告**
     （"这看起来是旧版 bool 用法，请改用 `_bool`"），不是让你自己猜。
   - 数字类型的 `_b` 列请把单元格设成**文本格式**：Excel 数字格式只保留 15 位有效数字。
2. **生成的 Getter 改用 Newtonsoft.Json 反序列化**（原来是 `JsonUtility`）——
   因为 JsonUtility 读不回 `BigInteger` / `decimal` / `DateTime` / `Guid` / `Dictionary` / `Rect`（实测全是 `{}`）。
   本包 `package.json` 新增依赖 `com.unity.nuget.newtonsoft-json`；生成的 JSON **格式不变**（仍是 `{"data":[...]}`）。
3. **增量生成带上模板版本**：升级到本版后，所有表会**自动重新生成一次**（否则"表没改"会一直跳过，产物停在旧模板）。

### Added
- **列类型从 6 种扩到 27 种**（数组后缀 = 标量后缀 + `a`）：
  - 整数：`_i` `_l` **`_b`(BigInteger)** `_by` `_sb` `_sh` `_us` `_u` `_ul`
  - 小数：`_f` `_d` **`_dec`(decimal)**
  - 布尔/文本：`_bool` `_s` `_c`
  - 时间/标识：`_time` `_span` `_guid`
  - 枚举：`_e`（表内生成）、`_e:类型`（引用已编译枚举）、`_flags` / `_flags:类型`
  - 结构：`_v2` `_v3` `_v4` `_quat` `_color` `_rect`
  - 复合：`_kv`（`Dictionary<string,string>`）
- **枚举生成**（`_e` / `_ea`）：不带值从 0 起按首次出现顺序自动编号；`green_3` = 显式值 3（按**最后一个下划线**拆，
  所以 `fire_dragon` 是完整名字）；生成 `名字 = 表名 + 字段名` 的枚举（`Building` 的 `State_e` → `BuildingState`）。
  成员名自动转 PascalCase（改名会给警告），`_flags` 用 `|` 组合、JSON 存按位或的数字。
- **枚举校验**（全部是错误级、阻塞生成）：
  - 「**不能有同一枚举值**」（两个成员同值）、同名被赋两个不同的值、取值取不出合法成员名、枚举列一个取值都没有；
  - 引用模式 `_e:类型` 会按**已编译的枚举**校验类型与成员是否存在（找不到会列出可用成员）；
  - **跨表重名**（同名枚举成员不一致会生成重复 C# 类型 CS0101）在**同一文件和跨文件**都会拦下。
- **章节表枚举取并集**：`前缀_数字` 组内各章节的枚举取值合并后统一编号（不是各建一套），
  枚举类型用章节前缀命名、定义写在基类文件里，各章节共用。
- **严格类型校验**（`CExcelGenerateOptions.StrictTypeCheck`，默认开）：生成前每个单元格都按列声明类型真解析，
  填错就报**第几行第几列**并中止该表。以前 `Level_i` 填 `abc` 会被**安静地写成 0**，
  `Gold_b` 被 Excel 记成科学计数法会变成完全不同的数 —— 这类静默错数据比编译错误难查得多。
  老表迁移期可在窗口里关掉。
- **「类型映射说明」窗口全面升级**：按「整数 / 小数 / 布尔 / 文本 / 时间 / 枚举 / 结构 / 复合」分组渲染，
  可切换是否显示数组行，新增「枚举怎么写」专节（含完整例子），并可一键复制整张表为纯文本。
  还加了后端状态提示（没装 Newtonsoft 会红字提示 + 一键打开 Package Manager）。

### Changed
- `CExcelEnumBuilder` / `CExcelEnumResolver` / `CExcelEnumRegistry`：枚举定义构建、已编译枚举查找（带缓存）、
  一次生成内的重名登记。
- `CExcelCellJson`：**手写** JSON 字面量（校验与生成共用同一实现，所以"校验通过"一定"生成得出来"）。
  不用 `JsonConvert.SerializeObject`：它序列化 `Vector3`/`Rect` 会因 `normalized`/`center` 自引用直接抛异常。
- `CExcelTableValidator`：表级校验（值 + 枚举 + `_b` 迁移提示）。
- 生成的 Wrapper 从 `private` 改成 `public`：Newtonsoft 要能构造它（私有嵌套类型不可靠）。
- 生成产物文件头新增 `// Generator template: v2 (JSON backend: Newtonsoft.Json)`，能直接看出是哪版模板生成的。
- `GenerateFolder` / `GenerateAllSheets` / `Generate` 共享一次运行的**读结果缓存**与**枚举登记表**
  （章节表要读两遍：建枚举并集 + 生成）。
- 无后缀推断更保守：**整数但超出 `long` → string**（原来会掉进 double 静默丢精度），要大整数请显式写 `_b`。
- 数组分隔符按元素类型区分：向量/四元数/颜色/矩形**只能用 `;`**（元素内部就是逗号）；
  字典数组用 `|` 分组；`[Flags]` 数组先 `;` 拆元素、元素内再用 `|` 组合。
- 自动选主键时**排除数组列**（原来 `Tags_sa` 也会被当成主键列）。
- `CExcelGenerator.Validate(path, sheet, options)`：只校验不生成（预览窗口的"校验"按钮现在会做**真类型校验**）。
- 窗口：新增"严格类型校验"开关与后端状态；单文件预览窗口的"校验（不生成）"会跑真校验。
- `docs/design-excel.md` 按 v0.5.0 重写（类型表 / 枚举 / JSON 后端选型 / 踩坑记录）。

### Tests
- excel 测试 65 → **124**（全绿）。新增：
  - `CExcelJsonBackendTests`：**逐类型真反序列化**（单元格示例 → JSON → 真实 `JsonConvert.DeserializeObject`），
    并锁住"JsonUtility 读不回哪些"、"BigInteger 科学计数法有损"、颜色三种写法一致、四元数 4 数 vs 欧拉角、
    数组分隔符规则、空值默认值、转义、范围越界报错。
  - `CExcelEnumDefTests`：自动编号 / 显式值 / 拆分规则 / 成员名规范化 + 全部校验分支 + 引用模式 + 跨表重名 + 章节并集。
  - `CExcelEnumGenerationTests`：枚举端到端（生成枚举/字段/JSON 数字）、引用模式、`_flags`、`_flagsa`、
    章节并集、坏值报行号列名、`StrictTypeCheck=false` 降级、`_b` 迁移提示、空值默认。
- 记录了实测到的 Unity 行为：**普通 asmdef 会自动引用插件，测试程序集不会**
  （`optionalUnityReferences: ["TestAssemblies"]` 的 asmdef 拿不到 Newtonsoft）——
  所以测试通过编辑器程序集里的 `CExcelJsonBackend`（反射）走真实反序列化，而不是直接 `using Newtonsoft.Json`。

## [0.4.0] - 2026-09-18

### Added
- **「类型映射说明」窗口**（入口：`Window > CoffeeBean` → `Excel · 类型映射说明`，与「Excel 配置表工具」同级）：
  一张**当前可用**的后缀对照表（后缀 / C# 类型 / **单元格写法示例** / 说明，标量 + 数组各一行），
  外加「解析规则与边界」与「规划中 / 需要新后端」两区。

- **映射表变成代码里的单一数据源**（`CExcelTypeCatalog`）：后缀、C# 类型、示例、说明只写这一处，
  解析（`CExcelTypeInfer.FromSuffix` / `CSharpType` / 后缀长度）与窗口都从它读。
  以前"文档 / 窗口 / 解析"三处各写一遍必然漂移 —— 现在不可能对不上。

### Fixed
- **文档与实现不一致（测试抓出来的）**：旧文档写"整列 true/false/**1/0** → bool"，
  但 `Infer` 里 **int 优先于 bool**，所以纯 `1/0` 的列无后缀时其实是 **int**。
  现在窗口与表里都写明这个优先级（想要 bool 必须写 `_b`），并加测试锁住。
- `SuffixLength` 原来硬编码 `数组 ? 3 : 2` —— 加 `_bool`/`_boola` 这类更长后缀时会截错字段名。
  现在从映射表取实际长度。

### Tests
- 新增 `CExcelTypeCatalogTests`（12 条）：
  - 表本身完整（每个 kind 一条、后缀/类型/示例/说明都不缺、数组后缀 = 标量 + `a`）；
  - **与解析代码一致**：后缀往返、`CSharpType` 一致、数组编码符合 `IsArray/ElementKind` 约定；
  - **歧义防护**：任何后缀都不是另一个后缀的结尾（否则 `Count_ul` 会被 `_l` 抢匹配）——
    以后加 `_bool` vs `_b` 这类后缀时必须过这一关；
  - **示例真实可解析**（文档不能是编的）+ 显式锁住"1/0 无后缀 → int"的优先级；
  - 后缀长度取自表、字段名去后缀正确、纯文本导出覆盖全部条目。
- excel 测试 53 → 65；全量 EditMode **734 → 747**（746 通过 + 1 有意跳过）。

### Notes
- 这一版只加**说明窗口 + 数据源**，**没有**新增类型。枚举（`_e`）、`_b`→BigInteger、`_bool`、
  decimal/时间/Guid/Dictionary 等已在窗口的「规划中」区列明（含各自的阻塞点：换 Newtonsoft 后端）。
## [0.3.0] - 2026-09-17

### Added
- **模块标记 `[assembly: CoffeeBeanModule]`**，让 Core 能发现 excel 并纳入依赖图与版本兼容校验。
  此前 excel 既没有 `Runtime/` 也没有模块标记，`CoffeeBeanRegistry.Scan()` 扫不到它 ——
  在 `Window > CoffeeBean` 里看不到该模块，Core 的 `MinCoreVersion` 校验也覆盖不到它。

### 说明（为什么用 Editor-only 的 Bridge 程序集）
- 新增 `Runtime/Bridge/CoffeeBean.Excel.Bridge.asmdef` + `Bridge.cs`，
  与其它模块的 Bridge 一样受 `COFFEEBEAN_CORE` 约束（装了 Core 才编译），
  但**额外限定 `includePlatforms: ["Editor"]`** —— 与本包「Editor-only 配置表工具链」的定位一致：
  包里除 `Editor/` 外没有任何运行期代码，若把标记放进运行期程序集，
  玩家包体会白带一个什么都不做的 DLL。
- 结果：编辑器内 Module Manager 可见、版本可校验；玩家包体不含任何 excel 代码。
- 声明 `Dependencies = ["com.coffeebean.core"]`（标记本身就在 Core 存在时才编译）。

## [0.2.3] - 2026-08-28

### Changed
- **生成代码默认命名空间改为 `CoffeeBean` 根命名空间**：生成的表类/Getter 进 `CoffeeBean`，
  业务只需 `using CoffeeBean;` 即可访问（含框架所有模块主类型 + 配置表）
- **破坏性变更**：旧工程已生成代码是 `Config` 命名空间——升级后需在生成窗口把命名空间改回
  `CoffeeBean`（或保持 `Config`）并重新生成一次；`using Config;` 的旧代码需相应调整

## [0.2.2] - 2026-08-28

### Added
- **生成代码独立 asmdef**：生成时自动在代码输出目录创建 `{Namespace}.Generated.asmdef`（幂等，已存在不覆盖），
  把生成的表类/Getter 归入独立程序集——之后改配置表**只重编译生成程序集**（小、快），业务代码不重编译，
  加快迭代编译速度（比预编译 dll 更简单可靠：仍为源码、天然兼容 IL2CPP/调试/版本）

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
