using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WS_Modules
{
    internal sealed class EventSearchService : IEventSearchService
    {
        private const string RegisterPattern = "EventSystem.Register_";
        private const string TriggerPattern = "EventSystem.EventTrigger_";
        private const int MaxInvocationLineSpan = 16;

        public Dictionary<string, EventSystemInfo> SearchEventSystems()
        {
            var cache = new Dictionary<string, EventSystemInfo>();
            string scriptsRoot = Path.Combine(Application.dataPath, "Scripts");
            if (!Directory.Exists(scriptsRoot))
            {
                Debug.LogWarning($"[FrameSetting] Scripts folder not found: {scriptsRoot}");
                return cache;
            }

            foreach (var file in Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (ShouldSkipFile(file))
                {
                    continue;
                }

                if (!TryReadLines(file, out var lines))
                {
                    continue;
                }

                var script = LoadScript(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (TryCollectLineInfo(cache, lines, ref i, script, RegisterPattern, true))
                    {
                        continue;
                    }

                    TryCollectLineInfo(cache, lines, ref i, script, TriggerPattern, false);
                }
            }

            return cache;
        }

        private static bool ShouldSkipFile(string file)
        {
            string fileName = Path.GetFileName(file);
            return string.Equals(fileName, "EventSystem.cs", StringComparison.Ordinal) ||
                   string.Equals(fileName, "FrameSettingWindow.cs", StringComparison.Ordinal) ||
                   file.IndexOf($"{Path.DirectorySeparatorChar}FrameSettingWindow{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal) >= 0;
        }

        private static bool TryReadLines(string file, out string[] lines)
        {
            try
            {
                lines = File.ReadAllLines(file);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FrameSetting] Failed to read script: {file}. {ex.Message}");
                lines = Array.Empty<string>();
                return false;
            }
        }

        private static bool TryCollectLineInfo(Dictionary<string, EventSystemInfo> cache, string[] lines,
            ref int lineIndex, MonoScript script, string pattern, bool isRegister)
        {
            int sourceLine = lineIndex + 1;
            string line = lines[lineIndex];
            if (line.TrimStart().StartsWith("//") || !line.Contains(pattern, StringComparison.Ordinal))
            {
                return false;
            }

            string invocationText = string.Empty;
            int maxLineIndex = Math.Min(lines.Length - 1, lineIndex + MaxInvocationLineSpan - 1);
            for (int i = lineIndex; i <= maxLineIndex; i++)
            {
                invocationText += " " + lines[i].Trim();
                if (!TryExtractEventKeyExpression(invocationText, pattern, out string eventName))
                {
                    continue;
                }

                RecordEventInfo(cache, script, eventName, sourceLine, isRegister);
                lineIndex = i;
                return true;
            }

            if (!TryExtractEventKeyExpression(line, pattern, out string fallbackEventName))
            {
                return false;
            }

            RecordEventInfo(cache, script, fallbackEventName, sourceLine, isRegister);
            return true;
        }

        private static bool TryExtractEventKeyExpression(string line, string pattern, out string eventName)
        {
            eventName = string.Empty;

            int patternIndex = line.IndexOf(pattern, StringComparison.Ordinal);
            if (patternIndex == -1)
            {
                return false;
            }

            int openParenIndex = line.IndexOf('(', patternIndex + pattern.Length);
            if (openParenIndex == -1)
            {
                return false;
            }

            int depth = 0;
            int argumentStart = openParenIndex + 1;
            for (int i = argumentStart; i < line.Length; i++)
            {
                char ch = line[i];
                switch (ch)
                {
                    case '(':
                        depth++;
                        break;
                    case ')':
                        if (depth == 0)
                        {
                            eventName = line.Substring(argumentStart, i - argumentStart).Trim();
                            return !string.IsNullOrEmpty(eventName);
                        }
                        depth--;
                        break;
                    case ',':
                        if (depth == 0)
                        {
                            eventName = line.Substring(argumentStart, i - argumentStart).Trim();
                            return !string.IsNullOrEmpty(eventName);
                        }
                        break;
                }
            }

            return false;
        }

        private static MonoScript LoadScript(string file)
        {
            var relativePath = "Assets" + file.Replace(Application.dataPath, string.Empty).Replace("\\", "/");
            return AssetDatabase.LoadAssetAtPath<MonoScript>(relativePath);
        }

        private static void RecordEventInfo(Dictionary<string, EventSystemInfo> cache, MonoScript script, string eventName,
            int scLine, bool isRegister)
        {
            if (!cache.TryGetValue(eventName, out EventSystemInfo eventSystemInfo))
            {
                eventSystemInfo = new EventSystemInfo();
                cache[eventName] = eventSystemInfo;
            }

            AppendEventInfo(eventSystemInfo, script, scLine, isRegister);
        }

        private static void AppendEventInfo(EventSystemInfo eventSystemInfo, MonoScript script, int scLine,
            bool isRegister)
        {
            if (isRegister)
            {
                eventSystemInfo.registerLine.Add(scLine);
                eventSystemInfo.registerScripts.Add(script);
            }
            else
            {
                eventSystemInfo.triggerLine.Add(scLine);
                eventSystemInfo.triggerScripts.Add(script);
            }
        }
    }
}
