#if COFFEEBEAN_CORE
// CoffeeBean 模块标识 + Core 生命周期集成。
//
// 为什么本程序集是 Editor-only（与其它模块的 Bridge 不同）：
//   excel 是一条**纯编辑器**的配置表工具链（读表 → 推断类型 → 生成 JSON + C# 数据类 + Getter），
//   包里除 Editor/ 之外没有任何运行期代码。若把标记放进运行期程序集，
//   打包时会白带一个什么都不做的 DLL，也与本包「Editor-only」的定位相矛盾。
//   放在 Editor 程序集里，Module Manager / Core 在编辑器内即可发现本模块并做版本兼容校验，
//   而玩家包体里不含任何 excel 代码。
//
// 背景：此前 excel 既没有 Runtime/ 也没有模块标记，Core 完全发现不到它，
// 于是在 Module Manager 里看不到、也不会被版本兼容校验覆盖。
using CoffeeBean;

[assembly: CoffeeBeanModule(
    "com.coffeebean.excel",
    "0.3.0",
    DisplayName = "Excel",
    Description = "Editor-only config-table toolchain: Excel reading (MiniExcel), type inference, and generation of JSON + C# data classes + getters.",
    Dependencies = new[] { "com.coffeebean.core" }
)]

namespace CoffeeBean
{
    /// <summary>
    /// Core 集成：excel 是编辑器工具链，不提供服务实例，
    /// 本标记仅让 Core 能发现本模块、纳入依赖图与版本兼容校验。
    /// </summary>
    public sealed class ExcelModule : ICoffeeBeanModule
    {
        public void OnLoad(CoffeeBeanContext context)
        {
            context.Log("CoffeeBean.Excel integrated (editor-only toolchain).");
        }

        public void OnStart()
        {
        }

        public void OnShutdown()
        {
        }
    }
}
#endif
