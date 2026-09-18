using System.Collections.Generic;
using System.IO;
using MiniExcelLibs;
using UnityEditor;
using UnityEngine;

namespace CoffeeBean.Excel.Demo
{
    /// <summary>
    /// Excel 工具示例（Editor 菜单）：一键生成演示配置表并跑通完整流程——
    /// 创建样例 xlsx（MiniExcel 写出）→ CExcelReader 读取 → CExcelGenerator 生成。
    /// 菜单：Tools/CoffeeBean Excel/生成示例配置表
    /// </summary>
    public static class ExcelDemo
    {
        private const string DemoFolder = "Assets/ExcelDemo";

        [MenuItem("Tools/CoffeeBean Excel/生成示例配置表")]
        public static void GenerateDemoTable()
        {
            if (!AssetDatabase.IsValidFolder(DemoFolder))
                AssetDatabase.CreateFolder("Assets", "ExcelDemo");

            // 1. 造一张样例表（列名带类型后缀；表头中文说明行可选——若存在会作为生成代码的字段注释）
            string xlsxPath = Path.Combine(DemoFolder, "ChapterConfig.xlsx");
            var rows = new List<IDictionary<string, object>>
            {
                DemoRow("Id_i", 1, "Name_s", "第一章 晨曦山谷", "StageCount_i", 5, "Rewards_ia", "100;200;300"),
                DemoRow("Id_i", 2, "Name_s", "第二章 迷雾森林", "StageCount_i", 8, "Rewards_ia", "400;500"),
                DemoRow("Id_i", 3, "Name_s", "第三章 熔火地窟", "StageCount_i", 12, "Rewards_ia", ""),
            };
            MiniExcel.SaveAs(xlsxPath, rows, overwriteFile: true);
            AssetDatabase.Refresh();

            // 2. 读取 + 预览
            CExcelReadResult read = CExcelReader.Read(xlsxPath);
            Debug.Log($"[ExcelDemo] 读取完成：表头第 {read.HeaderRowIndex + 1} 行，数据 {read.Rows.Count} 行，问题 {read.Issues.Count} 条");
            if (read.HasBlockingErrors)
            {
                Debug.LogError("[ExcelDemo] 读取失败:\n" + string.Join("\n", read.Issues));
                return;
            }

            // 3. 生成产物（代码 → 内嵌包；数据 → 同包的 <表>/Data 下，Assets 不参与）
            var options = new CExcelGenerateOptions
            {
                CodeFolder = "Packages/com.coffeebean.excel.demo.generated",
                PackageName = "com.coffeebean.excel.demo.generated",
                Namespace = "CoffeeBean", // 统一命名空间：using CoffeeBean; 即可访问生成的表类
            };
            CExcelGenerateResult result = CExcelGenerator.Generate(xlsxPath, options);
            if (result.Success)
            {
                Debug.Log("[ExcelDemo] 生成完成:\n" + string.Join("\n", result.GeneratedFiles));
                Debug.Log("[ExcelDemo] 生成的 Getter 用法：ChapterConfigGetter.Get(1) / ChapterConfigGetter.All" +
                          "（运行时先跑一次 ConfigTableRuntime.PreloadAll() 协程）");
            }
            else
            {
                Debug.LogError("[ExcelDemo] 生成失败:\n" + string.Join("\n", result.Issues));
            }
        }

        private static Dictionary<string, object> DemoRow(params object[] keyValues)
        {
            var row = new Dictionary<string, object>();
            for (int i = 0; i + 1 < keyValues.Length; i += 2)
                row[(string)keyValues[i]] = keyValues[i + 1];
            return row;
        }
    }
}
