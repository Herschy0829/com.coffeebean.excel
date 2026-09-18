using System;
using System.Collections.Generic;
using System.Reflection;

namespace CoffeeBean
{
    /// <summary>
    /// 测试/诊断用的反序列化探针（<c>internal</c>；测试程序集靠 <c>InternalsVisibleTo</c> 使用）。
    ///
    /// **它存在的唯一理由**：Unity 不会给测试程序集自动引用插件 DLL
    /// （`optionalUnityReferences: ["TestAssemblies"]` 的 asmdef 拿不到 Newtonsoft），
    /// 所以"真反序列化"这一步必须放在**编辑器程序集**里做，测试再通过它验证后端能力。
    /// 详见 <see cref="CExcelJsonBackend"/> 的注释。
    /// </summary>
    internal static class CExcelJsonProbe
    {
        /// <summary>把 catalog 里的 C# 类型名解析成 Type（含 <c>X[]</c> 与 <c>Dictionary&lt;string,string&gt;</c>）。</summary>
        public static Type ResolveType(string csharpTypeName)
        {
            if (string.IsNullOrEmpty(csharpTypeName)) return null;

            string name = csharpTypeName.Trim();
            if (name.EndsWith("[]", StringComparison.Ordinal))
            {
                Type element = ResolveType(name.Substring(0, name.Length - 2));
                return element?.MakeArrayType();
            }

            switch (name)
            {
                case "int": return typeof(int);
                case "long": return typeof(long);
                case "float": return typeof(float);
                case "double": return typeof(double);
                case "bool": return typeof(bool);
                case "string": return typeof(string);
                case "char": return typeof(char);
                case "byte": return typeof(byte);
                case "sbyte": return typeof(sbyte);
                case "short": return typeof(short);
                case "ushort": return typeof(ushort);
                case "uint": return typeof(uint);
                case "ulong": return typeof(ulong);
                case "decimal": return typeof(decimal);
                case "Dictionary<string,string>": return typeof(Dictionary<string, string>);
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type exact = SafeGetType(assembly, name);
                if (exact != null) return exact;
            }
            return null;
        }

        /// <summary>按 catalog 的 C# 类型名反序列化（真实走 Newtonsoft）。</summary>
        public static object Deserialize(string json, string csharpTypeName)
        {
            Type type = ResolveType(csharpTypeName);
            if (type == null) throw new InvalidOperationException("无法解析 C# 类型名：" + csharpTypeName);
            return CExcelJsonBackend.Deserialize(json, type);
        }

        /// <summary>JSON 文本是否合法。</summary>
        public static bool IsValidJson(string json) => CExcelJsonBackend.IsValidJson(json);

        private static Type SafeGetType(Assembly assembly, string name)
        {
            try
            {
                return assembly.GetType(name, false);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
