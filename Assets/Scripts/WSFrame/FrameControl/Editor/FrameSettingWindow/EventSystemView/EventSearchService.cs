using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WS_Modules
{
    /// <summary>
    /// 遍历脚本并将 EventSystem、BusinessArchitecture 调用归并为事件索引。
    /// </summary>
    internal sealed class EventSearchService : IEventSearchService
    {
        private readonly EventSourceParser _parser = new EventSourceParser();

        /// <summary>扫描所有业务脚本中的事件注册和发布位置。</summary>
        /// <returns>按中心种类和事件身份归并的结果。</returns>
        public Dictionary<string, EventSystemInfo> SearchEventSystems()
        {
            var cache = new Dictionary<string, EventSystemInfo>(StringComparer.Ordinal);
            string scriptsRoot = Path.Combine(Application.dataPath, "Scripts");
            if (!Directory.Exists(scriptsRoot))
            {
                Debug.LogWarning($"[FrameSetting] Scripts folder not found: {scriptsRoot}");
                return cache;
            }

            foreach (string file in Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (ShouldSkipFile(file) || !TryReadLines(file, out string[] lines)) continue;
                MonoScript script = LoadScript(file);
                foreach (ParsedEventCall call in _parser.Parse(lines, script))
                {
                    string key = BuildEventKey(call, file);
                    if (!cache.TryGetValue(key, out EventSystemInfo info))
                    {
                        info = new EventSystemInfo();
                        info.Center = call.Center;
                        info.IsGenericForwarding = call.IsGenericForwarding;
                        string expression = string.IsNullOrEmpty(call.Expression) ? call.DisplayText : call.Expression;
                        info.DisplayName = $"{call.Center}: {expression}";
                        info.Tooltip = call.IsGenericForwarding
                            ? "此处使用的是 Publish<TEvent> 一类泛型转发，TEvent 的具体类型由调用方决定；该条目只记录转发位置。"
                            : info.DisplayName;
                        cache.Add(key, info);
                    }

                    var callInfo = new EventCallInfo(call.Script, call.Line, call.Source, call.Center, call.DisplayText);
                    callInfo.IsGenericForwarding = call.IsGenericForwarding;
                    if (call.IsRegister) info.AddRegister(callInfo); else info.AddTrigger(callInfo);
                }
            }
            return cache;
        }

        /// <summary>排除事件框架实现和当前面板代码，避免显示内部转发调用。</summary>
        /// <param name="file">脚本路径。</param><returns>是否跳过。</returns>
        private static bool ShouldSkipFile(string file)
        {
            string fileName = Path.GetFileName(file);
            return string.Equals(fileName, "EventSystem.cs", StringComparison.Ordinal) ||
                   string.Equals(fileName, "BusinessArchitecture.cs", StringComparison.Ordinal) ||
                   string.Equals(fileName, "FrameSettingWindow.cs", StringComparison.Ordinal) ||
                   file.IndexOf($"{Path.DirectorySeparatorChar}FrameSettingWindow{Path.DirectorySeparatorChar}", StringComparison.Ordinal) >= 0;
        }

        /// <summary>读取脚本内容并在文件不可读时跳过该文件。</summary>
        /// <param name="file">脚本路径。</param><param name="lines">读取出的行。</param><returns>是否成功。</returns>
        private static bool TryReadLines(string file, out string[] lines)
        {
            try { lines = File.ReadAllLines(file); return true; }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FrameSetting] Failed to read script: {file}. {ex.Message}");
                lines = Array.Empty<string>(); return false;
            }
        }

        /// <summary>加载脚本资源以便面板提供对象字段和源码跳转。</summary>
        /// <param name="file">绝对路径。</param><returns>脚本资源。</returns>
        private static MonoScript LoadScript(string file)
        {
            string relativePath = "Assets" + file.Replace(Application.dataPath, string.Empty).Replace("\\", "/");
            return AssetDatabase.LoadAssetAtPath<MonoScript>(relativePath);
        }

        /// <summary>构造中心种类与事件身份组成的稳定索引键。</summary>
        /// <param name="call">解析结果。</param><param name="file">来源文件。</param><returns>索引键。</returns>
        private static string BuildEventKey(ParsedEventCall call, string file)
        {
            string center = call.Center.ToString();
            if (call.IsGenericForwarding) return "Generic:" + center + ":" + file + ":" + call.Line + ":" + call.Expression;
            if (!string.IsNullOrEmpty(call.Expression)) return center + ":" + call.Expression;
            return "Unresolved:" + center + ":" + file + ":" + call.Line;
        }
    }
}
