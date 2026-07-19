using System.Text;
using UnityEngine;

namespace WS_Modules.LogModule
{
    public class UnityLogger : ILogger
    {
        private static string infoLogColor = "#FFFFFF"; // 白色   
        private static string successLogColor = "#00FF00"; // 绿色
        private static string warningLogColor = "#FFFF00"; // 黄色
        private static string errorLogColor = "#FF0000"; // 红色
        
        public void SetLogColor(string logInfoColor, string logSuccessColor, string logWarningColor, string logErrorColor)
        {
            UnityLogger.infoLogColor = logInfoColor;
            UnityLogger.successLogColor = logSuccessColor;
            UnityLogger.warningLogColor = logWarningColor;
            UnityLogger.errorLogColor = logErrorColor;
        }

        public void Log(string message)
        {
            Debug.Log(DecorateLog(LogLevel.Info,message));
        }

        public void LogSuccess(string message)
        {
            Debug.Log(DecorateLog(LogLevel.Success,message));
        }

        public void LogWarning(string message)
        {
            Debug.LogWarning(DecorateLog(LogLevel.Warning,message));
        }

        public void LogError(string message)
        {
            Debug.LogError(DecorateLog(LogLevel.Error,message));
        }

        private string DecorateLog(LogLevel logLevel,string msg)
        {
            // 按行分割日志内容，对每一行添加颜色标签，然后再拼接起来，以支持多行日志的颜色显示
            // Unity 的控制台对多行日志的支持不是很好，需要手动处理，否则颜色标签会混乱
            var strArray = msg.Split('\n');
            StringBuilder msgBuilder = new();
            switch (logLevel)   
            {
                case LogLevel.Info:
                    foreach (var str in strArray)
                    {
                        msgBuilder.Append($"<color={infoLogColor}>{str}</color>\n");
                    }
                    break;
                case LogLevel.Success:
                    foreach (var str in strArray)
                    {
                        msgBuilder.Append($"<color={successLogColor}>{str}</color>\n");
                    }
                    break;
                case LogLevel.Warning:
                    foreach (var str in strArray)
                    {
                        msgBuilder.Append($"<color={warningLogColor}>{str}</color>\n");
                    }
                    break;
                case LogLevel.Error:
                    foreach (var str in strArray)
                    {
                        msgBuilder.Append($"<color={errorLogColor}>{str}</color>\n");
                    }
                    break;
                default:
                    foreach (var str in strArray)
                    {
                        msgBuilder.Append($"<color={infoLogColor}>{str}</color>\n");
                    }
                    break;
            }
            return msgBuilder.ToString();
        }
    }
}