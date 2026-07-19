#if UNITY_EDITOR
using System.Text.RegularExpressions;
using UnityEngine;

namespace WS_Modules.LogModule
{
    public static class LogEditorJump
    {
        [UnityEditor.Callbacks.OnOpenAssetAttribute(0)]
        static bool OnOpenAsset(int instanceID, int line)
        {
            var stackTrace = GetStackTrace();
            Debug.Log("StackTrace: " + stackTrace);

            if (string.IsNullOrEmpty(stackTrace)) return false;

            // This pattern is designed to capture file paths from a Unity stack trace.
            // It looks for lines starting with "at " and captures the path and line number.
            // It handles both absolute paths (like D:\...) and Unity project-relative paths (like Assets/...).
            string pattern = @"(?:\s*at\s+)?.*:.*\s*\(.*\)\s*\(at\s+((?:D:/|Assets/)[^:]+):(\d+)\)";
            var matches = Regex.Matches(stackTrace, pattern);

            foreach (Match match in matches)
            {
                var path = match.Groups[1].Value.Replace("\\", "/");
                var lineStr = match.Groups[2].Value;
                Debug.Log("Found stack frame: " + path + " Line: " + lineStr);

                // Skip any stack frame that is part of the LogModule itself.
                if ((path.Contains("WSFrame/Core/LogModule") || path.Contains("WSFrame/LogModule")) || path.Contains("WSLog.cs") || path.Contains("LogEditorJump.cs") || path.Contains("LogManager.cs") || path.Contains("UnityLogger.cs"))
                {
                    continue;
                }
                

                // int.TryParse(lineStr, out var tempLine);
                // Debug.Log("line: " + tempLine);
                if (int.TryParse(lineStr, out var row))
                {
                    string fullPath = path;
                    // If the path is relative (starts with "Assets/"), convert it to a full system path.
                    if (path.StartsWith("Assets/"))
                    {
                        fullPath = Application.dataPath + path.Substring("Assets".Length);
                    }
                    // Debug.Log($"Jumping to: {fullPath} at line {row}");
                    // Use Unity's internal utility to open the file at the specified line.
                    UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(fullPath, row);
                    return true; // Stop after the first successful jump.
                }
            }
            Debug.Log("No suitable stack frame found for jumping.");
            return false; // Return false if no suitable stack frame was found.
        }

        /// <summary>
        /// 获取当前日志窗口选中的日志的堆栈信息
        /// </summary>
        /// <returns>堆栈文本</returns>
        private static string GetStackTrace()
        {
            var consoleWindowType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.ConsoleWindow");
            var consoleWindowFieldInfo = consoleWindowType.GetField("ms_ConsoleWindow",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            if (consoleWindowFieldInfo != null)
            {
                var consoleWindow = consoleWindowFieldInfo.GetValue(null) as UnityEditor.EditorWindow;

                if (consoleWindow != UnityEditor.EditorWindow.focusedWindow) return null;

                var activeTextFieldInfo = consoleWindowType.GetField(
                    "m_ActiveText",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                if (activeTextFieldInfo != null) return activeTextFieldInfo.GetValue(consoleWindow).ToString();
            }

            return null;
        }
    }
}
#endif
