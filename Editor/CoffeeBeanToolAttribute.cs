using System;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// CoffeeBean 工具窗口标记（与 core 中同名类对应，解耦复制）：
    /// 打上此标记的 EditorWindow 会被 CoffeeBean Hub 窗口（Window &gt; CoffeeBean）自动发现并列出入口。
    /// 本模块**不编译期依赖 core**，此类型由各模块各自维护（全名一致即可被反射识别）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CoffeeBeanToolAttribute : Attribute
    {
        public string Title { get; }
        public string Description { get; }
        public string Module { get; }

        public CoffeeBeanToolAttribute(string title, string description = "", string module = "")
        {
            Title = title;
            Description = description;
            Module = module;
        }
    }
}
