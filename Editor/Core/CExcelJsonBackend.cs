using System;
using System.Reflection;

namespace CoffeeBean
{
    /// <summary>
    /// JSON 后端（Newtonsoft.Json）访问层。
    ///
    /// **为什么全用反射**：excel 自己是编辑器工具链，编译**不依赖** Newtonsoft ——
    /// 只有**生成出来的**代码才需要它。这样"项目里没装"只会让生成产物编译不过（窗口里有明确提示 +
    /// 一键打开 Package Manager），而不会让 excel 工具本身编译不过（那会连提示都看不到）。
    ///
    /// 顺带一个实测到的 Unity 行为（踩过一次，记在这里）：
    /// **普通 asmdef 会自动引用插件**（`overrideReferences: false` 时 Newtonsoft/ MiniExcel 都拿得到），
    /// 但 **测试程序集拿不到** —— `optionalUnityReferences: ["TestAssemblies"]` 的 asmdef 编译时
    /// 不会自动引用插件 DLL，于是 `using Newtonsoft.Json` 会 CS0246。
    /// 所以测试不直接引用 Newtonsoft，而是通过本类（编辑器程序集里的反射调用）走真实反序列化。
    /// </summary>
    public static class CExcelJsonBackend
    {
        /// <summary>包名（提示用户装什么）。</summary>
        public const string PackageName = "com.unity.nuget.newtonsoft-json";

        private static bool _probed;
        private static Assembly _assembly;
        private static Type _jsonConvert;
        private static MethodInfo _deserializeOneArg;
        private static string _probeError;

        /// <summary>项目里是否有 Newtonsoft.Json（生成的代码必需）。</summary>
        public static bool IsAvailable
        {
            get
            {
                Probe();
                return _assembly != null;
            }
        }

        /// <summary>版本号（取不到返回 "?"）。</summary>
        public static string Version
        {
            get
            {
                Probe();
                try
                {
                    return _assembly?.GetName().Version?.ToString() ?? "?";
                }
                catch (Exception)
                {
                    return "?";
                }
            }
        }

        /// <summary>探测失败的原因（给窗口/测试用）。</summary>
        public static string ProbeError
        {
            get
            {
                Probe();
                return _probeError;
            }
        }

        /// <summary>一句话状态（窗口里显示）。</summary>
        public static string Describe()
            => IsAvailable
                ? $"Newtonsoft.Json {Version}（已安装，生成的 Getter 可正常读 BigInteger / decimal / 时间 / Guid / 字典 / Vector / Color / Rect）"
                : $"未找到 Newtonsoft.Json —— 生成的 Getter 需要 {PackageName}，请先装上（Window > Package Manager > + > Add package by name）";

        /// <summary>
        /// 真反序列化（反射调用 <c>JsonConvert.DeserializeObject&lt;T&gt;(string)</c>）。
        /// 生成的 Getter 走的就是这条路径 —— 测试用它验证"映射表承诺的类型后端真读得回来"。
        /// </summary>
        public static object Deserialize(string json, Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            Probe();
            if (_assembly == null)
                throw new InvalidOperationException("Newtonsoft.Json 不可用：" + _probeError);

            if (_deserializeOneArg == null)
            {
                foreach (MethodInfo method in _jsonConvert.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name != "DeserializeObject" || !method.IsGenericMethodDefinition) continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                    {
                        _deserializeOneArg = method;
                        break;
                    }
                }
                if (_deserializeOneArg == null)
                    throw new InvalidOperationException("Newtonsoft.Json 的 JsonConvert.DeserializeObject<T>(string) 没找到");
            }

            try
            {
                return _deserializeOneArg.MakeGenericMethod(type).Invoke(null, new object[] { json });
            }
            catch (TargetInvocationException e)
            {
                throw e.InnerException ?? e;
            }
        }

        /// <summary>JSON 文本是否合法（解析一次；不合法返回 false 而不是抛）。</summary>
        public static bool IsValidJson(string json)
        {
            try
            {
                Deserialize(json, typeof(object));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Probe()
        {
            if (_probed) return;
            _probed = true;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(assembly.GetName().Name, "Newtonsoft.Json", StringComparison.Ordinal)) continue;
                Type convert = assembly.GetType("Newtonsoft.Json.JsonConvert", false);
                if (convert == null) continue;
                _assembly = assembly;
                _jsonConvert = convert;
                return;
            }

            _probeError = "已加载的程序集里没有 Newtonsoft.Json（包未安装或尚未编译）";
        }
    }
}
