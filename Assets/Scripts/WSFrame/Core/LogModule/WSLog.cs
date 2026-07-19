using System.Diagnostics;
using UnityEngine;

namespace WS_Modules.LogModule
{
    /// <summary>
    /// 使用外观模式简化日志记录操作的自定义日志类。
    /// </summary>
    public static class WSLog
    {
        private static bool isInitialized;

        public static bool EnableWriteTime { get; private set; } = true;

        static WSLog()
        {
            InitDefault();
        }

        private static void InitDefault()
        {
            EnableWriteTime = true;
            LogManager.Initialize();
            isInitialized = true;
        }

        public static void Init(LogSettings logSetting)
        {
            logSetting ??= new LogSettings();
            EnableWriteTime = logSetting.EnableWriteTime;

            LogManager.Initialize(
                "#" + ColorUtility.ToHtmlStringRGB(logSetting.InfoColor),
                "#" + ColorUtility.ToHtmlStringRGB(logSetting.SucceedColor),
                "#" + ColorUtility.ToHtmlStringRGB(logSetting.WarningColor),
                "#" + ColorUtility.ToHtmlStringRGB(logSetting.ErrorColor),
                logSetting.EnableWriteTime,
                logSetting.EnableWriteThreadID,
                logSetting.EnableWriteTrace,
                logSetting.EnableSaveToFile,
                logSetting.SaveLogTypes,
                logSetting.CustomSaveFileName,
                Application.persistentDataPath + logSetting.SavePath,
                LoggerType.Unity,
                5);

            isInitialized = true;
        }

        [Conditional("WS_LOG_ENABLED")]
        public static void Log(string message)
        {
            if (!isInitialized)
                InitDefault();
            LogManager.Log(message);
        }

        [Conditional("WS_LOG_ENABLED")]
        public static void LogSuccess(string message)
        {
            if (!isInitialized)
                InitDefault();
            LogManager.Succeed(message);
        }

        [Conditional("WS_LOG_ENABLED")]
        public static void LogWarning(string message)
        {
            if (!isInitialized)
                InitDefault();
            LogManager.Warning(message);
        }

        [Conditional("WS_LOG_ENABLED")]
        public static void LogError(string message)
        {
            if (!isInitialized)
                InitDefault();
            LogManager.Error(message);
        }
    }
}
