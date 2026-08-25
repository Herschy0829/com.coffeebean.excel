using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace CoffeeBean.Excel
{
    /// <summary>
    /// 增量生成状态：按文件最后修改时间记录"已生成"标记，未变化的表跳过重新生成
    /// （对齐 Idle 的 IsFileChange / SaveExcleMD5 机制）。
    /// 状态存 EditorPrefs（键 = 文件绝对路径），不污染项目资产。
    /// </summary>
    public static class CExcelIncrementalGenerator
    {
        private const string PrefPrefix = "CoffeeBean.Excel.LastWrite.";
        private const string TrackedFilesKey = "CoffeeBean.Excel.TrackedFiles";
        private const char Separator = '|';

        /// <summary>文件是否自上次生成后发生变化（未记录过 / 修改时间不一致 = 已变化）。</summary>
        public static bool IsChanged(string excelPath)
        {
            if (string.IsNullOrEmpty(excelPath) || !File.Exists(excelPath)) return false;
            string recorded = EditorPrefs.GetString(Key(excelPath), string.Empty);
            if (recorded.Length == 0) return true;
            return recorded != LastWriteTicks(excelPath);
        }

        /// <summary>记录"已按当前版本生成"（供下次对比）。</summary>
        public static void MarkGenerated(string excelPath)
        {
            if (string.IsNullOrEmpty(excelPath) || !File.Exists(excelPath)) return;
            EditorPrefs.SetString(Key(excelPath), LastWriteTicks(excelPath));
            Track(excelPath);
        }

        /// <summary>清空指定文件的记录；path 为空时清空全部记录（强制全量重新生成）。</summary>
        public static void Clear(string excelPath = null)
        {
            if (!string.IsNullOrEmpty(excelPath))
            {
                EditorPrefs.DeleteKey(Key(excelPath));
                return;
            }

            List<string> tracked = GetTrackedFiles();
            foreach (string path in tracked)
            {
                if (path.Length > 0) EditorPrefs.DeleteKey(Key(path));
            }
            EditorPrefs.DeleteKey(TrackedFilesKey);
        }

        private static string Key(string path) => PrefPrefix + path;

        private static string LastWriteTicks(string path)
            => File.GetLastWriteTimeUtc(path).Ticks.ToString();

        private static List<string> GetTrackedFiles()
        {
            var list = new List<string>();
            string raw = EditorPrefs.GetString(TrackedFilesKey, string.Empty);
            if (raw.Length == 0) return list;
            foreach (string part in raw.Split(Separator))
                if (part.Length > 0) list.Add(part);
            return list;
        }

        private static void Track(string path)
        {
            List<string> tracked = GetTrackedFiles();
            if (!tracked.Contains(path)) tracked.Add(path);
            EditorPrefs.SetString(TrackedFilesKey, string.Join(Separator.ToString(), tracked));
        }
    }
}
