using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.Build;
using UnityEngine;

namespace CoffeeBean
{
    /// <summary>
    /// 构建钩子：把生成包里的数据目录挂进产物的 StreamingAssets。
    ///
    /// **为什么必须有它**：数据生成在 Assets 之外（内嵌包 <c>Packages/&lt;包&gt;/&lt;表&gt;/Data/</c>）。
    /// Unity 官方文档明确：StreamingAssets 必须在 <c>Assets/StreamingAssets</c>，**包内的 StreamingAssets
    /// 文件夹不会被自动收录**。官方为"大且生成的内容"给的正解就是本钩子用的
    /// <c>BuildPlayerProcessor.PrepareForBuild</c> + <c>BuildPlayerContext.AddAdditionalPathToStreamingAssets</c>：
    /// 构建期把任意目录直接挂进产物，**不需要**镜像到 Assets，也**不需要**构建后清理。
    ///
    /// **发现方式**：扫描 <c>&lt;工程&gt;/Packages/*/coffeebean.configgen.json</c>（生成器产出的标记文件，
    /// 里面记着包名），把每个 <c>&lt;包&gt;/&lt;表&gt;/Data</c> 映射到 <c>StreamingAssets/&lt;包名&gt;/&lt;表&gt;/Data</c>。
    /// 这个映射必须与生成的 <c>ConfigTableRuntime.DataRoot</c> 的 Player 分支**逐字对齐**，否则运行时报"配置数据缺失"。
    ///
    /// **范围**：只扫描工程内的内嵌包（<c>Packages/</c> 下）。用 <c>file:</c> 引用的工程外包不在扫描范围内。
    /// </summary>
    internal sealed class CExcelBuildHook : BuildPlayerProcessor
    {
        public override int callbackOrder
        {
            get { return 0; }
        }

        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            try
            {
                List<DataMount> mounts = EnumerateDataMounts();
                for (int i = 0; i < mounts.Count; i++)
                    buildPlayerContext.AddAdditionalPathToStreamingAssets(mounts[i].Source, mounts[i].Destination);
            }
            catch (Exception e)
            {
                // 不阻断构建，但一定要响：漏挂数据的产物是"运行时才炸"的那种问题
                Debug.LogWarning("[CoffeeBean.Excel] 附加配置数据到 StreamingAssets 失败（产物可能缺配置）: " + e.Message);
            }
        }

        /// <summary>一个"数据目录 → 包内目标路径"的挂载点。</summary>
        internal struct DataMount
        {
            public string Source;
            public string Destination;
        }

        /// <summary>枚举所有生成包里的数据目录（供构建钩子与自检工具共用）。</summary>
        internal static List<DataMount> EnumerateDataMounts()
        {
            var mounts = new List<DataMount>();
            foreach (string packageRoot in FindMarkedPackages(out string packageName))
            {
                foreach (string tableDir in Directory.GetDirectories(packageRoot))
                {
                    string dataDir = Path.Combine(tableDir, "Data");
                    if (!Directory.Exists(dataDir)) continue;

                    string table = Path.GetFileName(tableDir);
                    mounts.Add(new DataMount
                    {
                        Source = dataDir.Replace('\\', '/'),
                        Destination = packageName + "/" + table + "/Data",
                    });
                }
            }
            return mounts;
        }

        /// <summary>找带标记文件的生成包。目前只支持工程内嵌包（Packages/ 下的真实目录）。</summary>
        private static List<string> FindMarkedPackages(out string packageName)
        {
            packageName = null;
            var result = new List<string>();

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string packagesRoot = Path.Combine(projectRoot, "Packages");
            if (!Directory.Exists(packagesRoot)) return result;

            foreach (string dir in Directory.GetDirectories(packagesRoot))
            {
                string marker = Path.Combine(dir, CExcelRuntimeTemplate.MarkerFileName);
                if (!File.Exists(marker)) continue;

                result.Add(dir);
                if (packageName == null) packageName = ReadMarkerPackageName(marker) ?? Path.GetFileName(dir);
            }
            return result;
        }

        /// <summary>从标记文件里取包名（极简解析：不为了一个字段引入 JSON 依赖）。</summary>
        private static string ReadMarkerPackageName(string markerPath)
        {
            try
            {
                string text = File.ReadAllText(markerPath);
                const string key = "\"packageName\"";
                int at = text.IndexOf(key, StringComparison.Ordinal);
                if (at < 0) return null;

                int colon = text.IndexOf(':', at + key.Length);
                if (colon < 0) return null;

                int open = text.IndexOf('"', colon + 1);
                if (open < 0) return null;

                int close = text.IndexOf('"', open + 1);
                if (close < 0) return null;

                string value = text.Substring(open + 1, close - open - 1).Trim();
                return value.Length == 0 ? null : value;
            }
            catch
            {
                return null;
            }
        }
    }
}
