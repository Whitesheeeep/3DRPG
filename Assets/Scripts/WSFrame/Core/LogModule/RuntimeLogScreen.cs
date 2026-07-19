using System;
using System.Collections.Generic;
using UnityEngine;

namespace WS_Modules.LogModule
{
    public class RuntimeScreenLogger : MonoBehaviour
    {
        private struct LogMessage
        {
            public string time;
            public string log;
            public string stackTrace;
            public UnityEngine.LogType logType;
            public bool fromWSLog;
            public bool isSuccess;
        }
        private List<LogMessage> logs = new List<LogMessage>();
        private GUIStyle mainContainerStyle;
        private GUIStyle logStyle;
        private GUIStyle warningStyle;
        private GUIStyle errorStyle;
        private GUIStyle logSelectStyle;
        private GUIStyle buttonStyle;
        private GUIStyle buttonSelectStyle;
        private GUIStyle stackTraceLableStyle;
        private GUIStyle lineStyle;
        private GUIStyle successStyle;
        public Font font;
        public int fontSize = 20;
        [Tooltip("是否根据屏幕分辨率自动缩放字体大小")]
        public bool autoAdjustFontSize = true;
        [Tooltip("自动缩放时的最小字号")]
        public int minAutoFontSize = 14;
        [Tooltip("自动缩放时的最大字号")]
        public int maxAutoFontSize = 32;
        [Tooltip("最多保留多少条日志显示在窗口中")]
        public int maxDisplayLines = 300;
        public bool show;
        public float margin = 20;
        public float width = 576;
        public float height = 400;
        private void Awake()
        {
            Init();
        }
        public void Init()
        {
            isFirstEnterOnGUI = true;
            // 创建初始样式
            Texture2D bgTex = new Texture2D(1, 1);
            Color bgColor = new Color(0, 0, 0, 0.5f);
            bgTex.SetPixel(0, 0, bgColor);
            bgTex.Apply();
            mainContainerStyle = new GUIStyle();
            mainContainerStyle.normal.background = bgTex;
            mainContainerStyle.font = font;
            mainContainerStyle.border = new RectOffset(5, 5, 5, 5);

            Texture2D lineTex = new Texture2D(1, 1);
            Color lineColor = new Color(0, 0, 0, 0.75f);
            lineTex.SetPixel(0, 0, bgColor);
            lineTex.Apply();
            lineStyle = new GUIStyle();
            lineStyle.normal.background = lineTex;


            logStyle = new GUIStyle();
            logStyle.font = font;
            logStyle.fontSize = fontSize;
            logStyle.normal.textColor = Color.white;
            logStyle.margin = new RectOffset(10, 5, 5, 5);

            warningStyle = new GUIStyle(logStyle);
            warningStyle.normal.textColor = Color.yellow;

            errorStyle = new GUIStyle(logStyle);
            errorStyle.normal.textColor = Color.red;

            logSelectStyle = new GUIStyle();
            logSelectStyle.font = font;
            logSelectStyle.fontSize = fontSize;
            logSelectStyle.normal.textColor = Color.gray;
            logSelectStyle.margin = new RectOffset(5, 5, 5, 5);

            successStyle = new GUIStyle(logStyle);
            successStyle.normal.textColor = Color.green;

            stackTraceLableStyle = new GUIStyle(logStyle);
            
            EnsureFontSizes(forceUpdate: true);

            // 初步绘制需要的数据
            width = Screen.width * 0.3f;
            height = Screen.height * 0.3f + stackTraceHeight;

            Application.logMessageReceived += HandleLog;
        }
        private void OnDestroy()
        {
            Application.logMessageReceived -= HandleLog;
        }

        private void HandleLog(string condition, string stackTrace, UnityEngine.LogType type)
        {
            stackTrace = ProcessingStackTraceString(stackTrace, out bool fromJKLog);
            condition = ProcessingJKLogString(condition);
            bool isSuccessLog = DetectSuccess(condition);
            LogMessage logMessage = new LogMessage()
            {
                log = condition,
                stackTrace = stackTrace,
                logType = type,
                time = DateTime.Now.ToString("HH:mm:ss"),
                fromWSLog = fromJKLog,
                isSuccess = isSuccessLog
            };
            logs.Add(logMessage);
            scrollToBottomPending = true;

            if (maxDisplayLines > 0 && logs.Count > maxDisplayLines)
            {
                int removeCount = logs.Count - maxDisplayLines;
                logs.RemoveRange(0, removeCount);
                if (selectIndex >= 0)
                {
                    selectIndex -= removeCount;
                    if (selectIndex < 0) selectIndex = -1;
                }
            }
            
            scrollToBottomPending = true;
        }

        private string ProcessingJKLogString(string logString)
        {
            if (logString.Length == 0) return logString;

            string newString = logString;
            // 去除最后一行可能的换行符
            if (logString[logString.Length - 1] == '\n')
            {
                newString = logString.Remove(logString.Length - 1, 1);
            }
            return newString;
        }
        private bool DetectSuccess(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            return message.IndexOf("[SUCCESS]", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private string ProcessingStackTraceString(string stackTrace, out bool fromJKLog)
        {
            int stackTrackRemoveIndex = -1;
            fromJKLog = stackTrace.Contains("WSLog");
            if (fromJKLog) // 去除4行JKLog带来的堆栈信息
            {
                for (int i = 0; i < 4; i++)
                {
                    stackTrackRemoveIndex = stackTrace.IndexOf("\n", stackTrackRemoveIndex + 1);
                }
            }
            else // 常规Debug.log 去除顶行堆栈信息
            {
                stackTrackRemoveIndex = stackTrace.IndexOf("\n");
            }
            stackTrace = stackTrace.Remove(0, stackTrackRemoveIndex + 1);
            return stackTrace;
        }

        private Vector2 stackTraceScrollPosition;
        private Vector2 logScrollPosition;

        private float posX;
        private float posY;
        private int selectIndex = -1;
        private float stackTraceHeight = 200;
        private bool showLog = true;
        private bool showWarning = true;
        private bool showError = true;
        

        private bool isFirstEnterOnGUI = true;
        private int cachedFontSize = -1;
        private bool scrollToBottomPending;

        private void OnGUI()
        {
            if (isFirstEnterOnGUI)
            {
                isFirstEnterOnGUI = false;
                buttonStyle = new GUIStyle(GUI.skin.button);
                buttonStyle.normal.textColor = Color.gray;
                buttonStyle.hover.textColor = Color.gray;

                buttonSelectStyle = new GUIStyle(GUI.skin.button);
                buttonSelectStyle.normal.textColor = Color.white;
                buttonSelectStyle.hover.textColor = Color.white;
            }

            EnsureFontSizes();

            posX = margin;
            posY = Screen.height - height - margin;
            if (show)
            {
                GUILayout.BeginArea(new Rect(posX, posY, width, height), mainContainerStyle);
                // 顶部菜单
                GUILayout.BeginHorizontal(GUILayout.Height(30));
                if (GUILayout.Button("Clear"))
                {
                    logs.Clear();
                }
                if (GUILayout.Button("Log", showLog ? buttonSelectStyle : buttonStyle))
                {
                    showLog = !showLog;
                }
                if (GUILayout.Button("Warning", showWarning ? buttonSelectStyle : buttonStyle))
                {
                    showWarning = !showWarning;
                }
                if (GUILayout.Button("Error", showError ? buttonSelectStyle : buttonStyle))
                {
                    showError = !showError;
                }
                if (GUILayout.Button("<<<Hide"))
                {
                    selectIndex = -1;
                    show = false;
                }
                GUILayout.EndHorizontal();
                // 滚动区域
                logScrollPosition = GUILayout.BeginScrollView(logScrollPosition, GUILayout.Width(width), GUILayout.Height(height - 30 - stackTraceHeight));
                for (int i = 0; i < logs.Count; i++)
                {
                    LogMessage logMessage = logs[i];

                    if (logMessage.logType == UnityEngine.LogType.Log && showLog == false) continue;
                    if (logMessage.logType == UnityEngine.LogType.Warning && showWarning == false) continue;
                    if (logMessage.logType == UnityEngine.LogType.Error && showError == false) continue;

                    GUIStyle normalLogStype;
                    switch (logMessage.logType)
                    {
                        case UnityEngine.LogType.Error:
                            normalLogStype = errorStyle;
                            break;
                        case UnityEngine.LogType.Warning:
                            normalLogStype = warningStyle;
                            break;
                        default:
                            normalLogStype = logStyle;
                            break;
                    }

                    string timeAndLog = null;
                    if (logMessage.fromWSLog)
                    {
                        // 根据框架设置决定是否显示时间
                        if (!WSLog.EnableWriteTime) timeAndLog = $"[{logMessage.time}]:{logMessage.log}";
                        else timeAndLog = logMessage.log;

                    }
                    else
                    {
                        timeAndLog = $"[{logMessage.time}]:{logMessage.log}";
                    }
                    if (i != selectIndex)
                    {
                        if (GUILayout.Button(timeAndLog, normalLogStype))
                        {
                            selectIndex = i;
                        }
                    }
                    else
                    {
                        if (GUILayout.Button(timeAndLog, logSelectStyle))
                        {
                            selectIndex = -1;
                        }
                    }
                }
                if (scrollToBottomPending)
                {
                    logScrollPosition.y = float.MaxValue;
                    scrollToBottomPending = false;
                }
                GUILayout.EndScrollView();
                // 堆栈信息
                GUILayout.Box("", lineStyle, GUILayout.Width(width), GUILayout.Height(3));
                stackTraceScrollPosition = GUILayout.BeginScrollView(stackTraceScrollPosition, GUILayout.Width(width), GUILayout.Height(stackTraceHeight - 10));
                if (selectIndex != -1)
                {
                    LogMessage logMessage = logs[selectIndex];
                    GUILayout.Label(logMessage.log, stackTraceLableStyle);
                    GUILayout.Label(logMessage.logType.ToString(), stackTraceLableStyle);
                    GUILayout.Label(logMessage.stackTrace, stackTraceLableStyle);
                }
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
            else
            {
                GUILayout.BeginArea(new Rect(margin, Screen.height - 60 - margin, 45, 45));
                if (GUILayout.Button("Log", showLog ? buttonSelectStyle : buttonStyle))
                {
                    show = true;
                }
                GUILayout.EndArea();
            }
        }

        private void EnsureFontSizes(bool forceUpdate = false)
        {
            int desiredSize = autoAdjustFontSize
                ? Mathf.Clamp(Mathf.RoundToInt(fontSize * (Screen.height / 1080f)), minAutoFontSize, maxAutoFontSize)
                : fontSize;

            if (!forceUpdate && cachedFontSize == desiredSize)
            {
                return;
            }

            cachedFontSize = desiredSize;
            logStyle.fontSize = desiredSize;
            warningStyle.fontSize = desiredSize;
            errorStyle.fontSize = desiredSize;
            logSelectStyle.fontSize = desiredSize;
            stackTraceLableStyle.fontSize = desiredSize;
        }

    }
}


