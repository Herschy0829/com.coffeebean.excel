using System.Collections.Generic;
using System.IO;
using MiniExcelLibs;
using UnityEditor;
using UnityEngine;

namespace CoffeeBean.Excel.Demo
{
    /// <summary>
    /// Excel 工具示例（Editor 菜单）：一键生成演示配置表并跑通完整流程——
    /// 创建样例 xlsx（MiniExcel 写出）→ CExcelReader 读取 → CExcelGenerator 生成三件套。
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

            // 1. 造一张样例表（"中文说明行 + 字段名行"双行表头，验证表头检测）
            string xlsxPath = Path.Combine(DemoFolder, "ChapterConfig.xlsx");
            MiniExcel.SaveAs(xlsxPath, new object[][]
            {
                new object[] { "章节ID", "章节名称", "关卡数", "奖励" },
                new object[] { "Id_i", "Name_s", "StageCount_i", "Rewards_ia" },
                new object[] { 1, "第一章 晨曦山谷", 5, "100;200;300" },
                new object[] { 2, "第二章 迷雾森林", 8, "400;500" },
                new object[] { 3, "第三章 熔火地窟", 12, "" },
                new object[] { "# 末尾注释：奖励为金币数量数组", null, null, null },
            });
            AssetDatabase.Refresh();

            // 2. 读取 + 预览
            CExcelReadResult read = CExcelReader.Read(xlsxPath);
            Debug.Log($"[ExcelDemo] 读取完成：表头第 {read.HeaderRowIndex + 1} 行，数据 {read.Rows.Count} 行，问题 {read.Issues.Count} 条");
            if (read.HasBlockingErrors)
            {
                Debug.LogError("[ExcelDemo] 读取失败:\n" + string.Join("\n", read.Issues));
                return;
            }

            // 3. 生成三件套
            var options = new CExcelGenerateOptions
            {
                OutputFolder = DemoFolder + "/Generated",
                Namespace = "Config",
            };
            CExcelGenerateResult result = CExcelGenerator.Generate(xlsxPath, options);
            if (result.Success)
            {
                AssetDatabase.Refresh();
                Debug.Log("[ExcelDemo] 生成完成:\n" + string.Join("\n", result.GeneratedFiles));
                Debug.Log("[ExcelDemo] 生成的 Getter 用法：ChapterConfigGetter.Get(1) / ChapterConfigGetter.All");
            }
            else
            {
                Debug.LogError("[ExcelDemo] 生成失败:\n" + string.Join("\n", result.Issues));
            }
        }
    }
}
