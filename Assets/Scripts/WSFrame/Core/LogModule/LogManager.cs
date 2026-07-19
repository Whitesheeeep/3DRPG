using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WS_Modules.LogModule
{
    /// <summary>
    /// 日志记录器接口
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// 使用 #RRGGBB 格式的字符串设置日志颜色
        /// </summary>
        void SetLogColor(string logInfoColor, string logSuccessColor, string logWarningColor, string logErrorColor);
        void Log(string message);
        void LogSuccess(string message);
        void LogWarning(string message);
        void LogError(string message);
    }

    /// <summary>
    /// 日志级别
    /// </summary>
    [Flags]
    public enum LogLevel
    {
        Info = 1 << 0,
        Success = 1 << 1,
        Warning = 1 << 2,
        Error = 1 << 3,
        All = Info | Success | Warning | Error
    }

    /// <summary>
    /// 日志记录器类型
    /// </summary>
    public enum LoggerType
    {
        Unity,
    }

    /// <summary>
    /// 日志管理器，用于配置和管理日志系统
    /// 最后 输出样例：“[ERROR] 2024-01-01 12:00:00.000 Thread:1 Your error message here
    ///                         at Namespace.Class.Method (FileName:LineNumber)”
    /// </summary>
    public static class LogManager
    {
        /// <summary>
        /// 当前使用的日志记录器
        /// </summary>
        private static ILogger logger;
        
        /// <summary>
        /// 日志默认颜色配置
        /// </summary>
        private static string infoLogColor = "#FFFFFF"; // 白色   
        private static string successLogColor = "#00FF00"; // 绿色
        private static string warningLogColor = "#FFFF00"; // 黄色
        private static string errorLogColor = "#FF0000"; // 红色

        /// <summary>
        /// 是否在日志中包含时间戳
        /// </summary>
        private static bool enableWriteTime = true;
        
        /// <summary>
        /// 要写入到文件中的日志级别
        /// </summary>
        private static LogLevel enableSaveslogLevel = LogLevel.All;
        
        /// <summary>
        /// 是否在日志中包含线程ID
        /// </summary>
        private static bool enableWriteThreadId = false;
        
        /// <summary>
        /// 跳过的堆栈帧数，用于获取调用日志方法的实际位置。
        /// 通常情况下，日志方法会被包装在其他方法中，因此需要跳过这些包装方法的堆栈帧。
        /// </summary>
        private static int skipTraceFrameCount = 4; 
        
        /// <summary>
        /// 是否在日志中包含堆栈跟踪信息
        /// </summary>
        private static bool enableWriteStackTrace = false;
        
        /// <summary>
        /// 是否启用日志文件保存
        /// </summary>
        private static bool enableSaveFile = false;
        
        /// <summary>
        /// 日志文件写入器
        /// </summary>
        private static StreamWriter logFileWriter;
        
        /// <summary>
        /// 文件写入锁，防止多线程写入冲突
        /// </summary>
        private static readonly object fileLock = new object();

        /// <summary>
        /// 初始化日志管理器
        /// </summary>
        /// <param name="infoLogColor">信息日志颜色</param>
        /// <param name="successLogColor">成功日志颜色</param>
        /// <param name="warningLogColor">警告日志颜色</param>
        /// <param name="errorLogColor">错误日志颜色</param>
        /// <param name="enableWriteTime">是否写入时间戳</param>
        /// <param name="writeThreadId">是否写入线程ID</param>
        /// <param name="writeStackTrace">是否写入堆栈跟踪</param>
        /// <param name="enableSaveFile">是否启用日志文件保存</param>
        /// <param name="wantSaveslogLevel">要保存的日志级别</param>
        /// <param name="customSaveLogFileName">自定义日志文件名 (不含扩展名)</param>
        /// <param name="customSaveLogFilePath">自定义日志文件保存路径</param>
        /// <param name="loggerType">日志记录器类型</param>
        /// <param name="skipTraceFrameCount">跳过的堆栈帧数</param>
        public static void Initialize(
            string infoLogColor = "#FFFFFF",
            string successLogColor = "#00FF00",
            string warningLogColor = "#FFFF00",
            string errorLogColor = "#FF0000",
            bool enableWriteTime = true,
            bool writeThreadId = false,
            bool writeStackTrace = false,
            bool enableSaveFile = false,
            LogLevel wantSaveslogLevel = LogLevel.All,
            string customSaveLogFileName = "",
            string customSaveLogFilePath = "",
            LoggerType loggerType = LoggerType.Unity,
            int skipTraceFrameCount = 4)
        {
            LogManager.enableWriteTime = enableWriteTime;
            LogManager.enableSaveslogLevel = wantSaveslogLevel;
            LogManager.enableWriteThreadId = writeThreadId;
            LogManager.enableWriteStackTrace = writeStackTrace;
            LogManager.enableSaveFile = enableSaveFile;
            LogManager.skipTraceFrameCount = skipTraceFrameCount;

            switch (loggerType)
            {
                case LoggerType.Unity:
                    logger = new UnityLogger();
                    logger.SetLogColor(infoLogColor, successLogColor, warningLogColor, errorLogColor);
                    break;
                default:
                    // 默认使用 UnityLogger
                    logger = new UnityLogger();
                    logger.SetLogColor(infoLogColor, successLogColor, warningLogColor, errorLogColor);
                    break;
            }
            
            Debug.Log("customSaveLogFilePath:"+ customSaveLogFilePath);
            SetupFile(customSaveLogFilePath, customSaveLogFileName);
        }

        private static void SetupFile(string customSaveLogFilePath, string customSaveLogFileName)
        {
            if (!enableSaveFile) return;

            try
            {
                string logDirectory = customSaveLogFilePath;
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                var fileName = string.IsNullOrEmpty(customSaveLogFileName)
                    ? $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_log.txt"
                    : customSaveLogFileName.EndsWith(".txt")? customSaveLogFileName : $"{customSaveLogFileName}.txt";

                string fileSavePath = Path.Combine(logDirectory, fileName);

                lock (fileLock)
                {
                    logFileWriter?.Dispose();
                    var fileStream = new FileStream(
                        fileSavePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite);
                    logFileWriter = new StreamWriter(fileStream, Encoding.UTF8)
                    {
                        AutoFlush = true
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LogManager] Failed to initialize log file: {ex.Message}");
                enableSaveFile = false;
            }
        }

        /// <summary>
        /// 记录普通信息日志
        /// </summary>
        /// <param name="msg">日志消息</param>
        public static void Log(string msg)
        {
            ProcessLog(LogLevel.Info, msg, logger.Log);
        }

        /// <summary>
        /// 记录警告日志
        /// </summary>
        /// <param name="msg">日志消息</param>
        public static void Warning(string msg)
        {
            ProcessLog(LogLevel.Warning, msg, logger.LogWarning);
        }

        /// <summary>
        /// 记录错误日志
        /// </summary>
        /// <param name="msg">日志消息</param>
        public static void Error(string msg)
        {
            ProcessLog(LogLevel.Error, msg, logger.LogError);
        }

        /// <summary>
        /// 记录成功日志
        /// </summary>
        /// <param name="msg">日志消息</param>
        public static void Succeed(string msg)
        {
            ProcessLog(LogLevel.Success, msg, logger.LogSuccess);
        }

        /// <summary>
        /// 处理并分发日志
        /// </summary>
        /// <param name="logLevel">日志级别</param>
        /// <param name="msg">原始消息</param>
        /// <param name="logAction">具体日志记录器的记录方法</param>
        private static void ProcessLog(LogLevel logLevel, string msg, Action<string> logAction)
        {
            string decoratedMsg = DecorateLog(logLevel, msg);
            logAction(decoratedMsg);

            if (enableSaveFile && (enableSaveslogLevel & logLevel) != 0)
            {
                WriteToFile(decoratedMsg);
            }
        }
        
        /// <summary>
        /// 装饰日志消息，添加额外信息
        /// </summary>
        /// <param name="logLevel">日志级别</param>
        /// <param name="msg">原始消息</param>
        /// <returns>装饰后的日志字符串</returns>
        private static string DecorateLog(LogLevel logLevel, string msg)
        {
            StringBuilder stringBuilder = new StringBuilder(256);
            
            stringBuilder.Append($"[{logLevel.ToString().ToUpper()}] ");

            if (enableWriteTime)
                stringBuilder.Append($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ");

            if (enableWriteThreadId)
                stringBuilder.Append($"Thread:{Thread.CurrentThread.ManagedThreadId} ");

            stringBuilder.Append(msg);

            if (enableWriteStackTrace)
                stringBuilder.Append(GetTrace());
            
            return stringBuilder.ToString();
        }

        /// <summary>
        /// 将日志写入文件
        /// </summary>
        /// <param name="text">要写入的文本</param>
        private static void WriteToFile(string text)
        {
            if (!enableSaveFile || logFileWriter == null) return;
            
            lock (fileLock)
            {
                logFileWriter.WriteLine(text);
            }
        }

        /// <summary>
        /// 获取堆栈跟踪信息
        /// </summary>
        /// <returns>格式化的堆栈跟踪字符串</returns>
        private static string GetTrace()
        {
            StringBuilder traceBuilder = new StringBuilder();
            StackTrace stackTrace = new StackTrace(skipTraceFrameCount, true);
            for (int i = 0; i < stackTrace.FrameCount; i++)
            {
                StackFrame frame = stackTrace.GetFrame(i);
                var method = frame.GetMethod();
                var declaringType = method.DeclaringType;
                // 避免记录Unity内部或System命名空间下的堆栈信息
                if (declaringType == null || (declaringType.Namespace != null && (declaringType.Namespace.StartsWith("UnityEngine") || declaringType.Namespace.StartsWith("System"))))
                {
                    continue;
                }
                traceBuilder.Append($"\n  at {declaringType.FullName}.{method.Name} ({frame.GetFileName()}:{frame.GetFileLineNumber()})");
            }
            return traceBuilder.ToString();
        }

        /// <summary>
        /// 关闭日志管理器，释放文件资源
        /// </summary>
        public static void Close()
        {
            lock (fileLock)
            {
                logFileWriter?.Dispose();
                logFileWriter = null;
            }
        }

        public static void Reset()
        {
            infoLogColor = "#FFFFFF"; // 白色
            successLogColor = "#00FF00"; // 绿色
            warningLogColor = "#FFFF00"; // 黄色
            errorLogColor = "#FF0000"; // 红色
            logger.SetLogColor(infoLogColor, successLogColor, warningLogColor, errorLogColor);
            enableWriteTime = true;
            enableWriteStackTrace = false;
            enableWriteThreadId = false;
            enableSaveslogLevel = LogLevel.All;
            enableSaveFile = false;
            skipTraceFrameCount = 4;
            lock (fileLock)
            {
                // 将文件写入器置空
                logFileWriter?.Flush();
                logFileWriter?.Dispose();
                logFileWriter = null;
            }
        }
    }
}

