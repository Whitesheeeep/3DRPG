using System;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace WS_Modules.LogModule
{
    [Serializable]
    public class LogSettings
    {
        [Header("Log 各种颜色")]
        public Color infoColor = Color.white;
        public Color warningColor = Color.yellow;
        public Color errorColor = Color.red;
        public Color succeedColor = Color.green;

        [LabelText("是否启用日志系统"), OnValueChanged("EnableLogValueChanged")]
        public bool enableLog = true;

        [LabelText("是否写入时间戳"), OnValueChanged("EnableLogValueChanged")]
        public bool enableWriteTime = true;

        [LabelText("是否写入线程ID"), OnValueChanged("EnableLogValueChanged")]
        public bool enableWriteThreadID = false;

        [LabelText("是否写入堆栈信息"), OnValueChanged("EnableLogValueChanged")]
        public bool enableWriteTrace = true;

        [LabelText("是否保存日志到文件"), OnValueChanged("EnableLogValueChanged")]
        public bool enableSaveToFile = false;

        [LabelText("保存日志类型"), HideIf("CheckSaveState"), OnValueChanged("EnableLogValueChanged")]
        public LogLevel saveLogTypes = LogLevel.All;

        [LabelText("自定义保存文件名（为空则使用默认文件名，会根据时间创建），并且为覆盖式的"), HideIf("CheckSaveState"),
         OnValueChanged("EnableLogValueChanged")]
        [InfoBox("自定义文件名会覆盖默认的按时间命名的日志文件，并且是覆盖式的保存")]
        public string customSaveFileName = "";

        [LabelText("保存路径（相对于持久化数据路径）"), HideIf("CheckSaveState"), OnValueChanged("EnableLogValueChanged")]
        public string savePath = "/WSFrame/Logs/";

        public Color InfoColor => infoColor;
        public Color SucceedColor => succeedColor;
        public Color WarningColor => warningColor;
        public Color ErrorColor => errorColor;
        public bool EnableWriteTime => enableWriteTime;
        public bool EnableWriteThreadID => enableWriteThreadID;
        public bool EnableWriteTrace => enableWriteTrace;
        public bool EnableSaveToFile => enableSaveToFile;
        public LogLevel SaveLogTypes => saveLogTypes;
        public string CustomSaveFileName => customSaveFileName;
        public string SavePath => savePath;

        public LogSettings()
        {
        }

        public LogSettings(
            Color infoColor,
            Color succeedColor,
            Color warningColor,
            Color errorColor,
            bool enableWriteTime,
            bool enableWriteThreadID,
            bool enableWriteTrace,
            bool enableSaveToFile,
            LogLevel saveLogTypes,
            string customSaveFileName,
            string savePath)
        {
            this.infoColor = infoColor;
            this.succeedColor = succeedColor;
            this.warningColor = warningColor;
            this.errorColor = errorColor;
            this.enableWriteTime = enableWriteTime;
            this.enableWriteThreadID = enableWriteThreadID;
            this.enableWriteTrace = enableWriteTrace;
            this.enableSaveToFile = enableSaveToFile;
            this.saveLogTypes = saveLogTypes;
            this.customSaveFileName = customSaveFileName;
            this.savePath = savePath;
        }

        public void Reset()
        {
            infoColor = Color.white;
            warningColor = Color.yellow;
            errorColor = Color.red;
            succeedColor = Color.green;

            enableLog = true;
            enableWriteTime = true;
            enableWriteThreadID = false;
            enableWriteTrace = true;
            enableSaveToFile = false;
            saveLogTypes = LogLevel.All;
            customSaveFileName = "";
            savePath = "/WSFrame/Logs/";
        }

#if UNITY_EDITOR
        /// <summary>
        /// 在编辑器中初始化设置变更监听。
        /// </summary>
        public void InitOnEditor()
        {
            EnableLogValueChanged();
        }

        [Button("打开日志保存目录"), HideIf("CheckSaveState")]
        private void OpenLogSaveDirectory()
        {
            string fullPath = savePath.StartsWith("/")
                ? Application.persistentDataPath + savePath
                : Application.persistentDataPath + "/" + savePath;
            System.IO.Directory.CreateDirectory(fullPath);
            EditorUtility.RevealInFinder(fullPath);
        }

        private bool CheckSaveState()
        {
            return !enableSaveToFile;
        }

        private void EnableLogValueChanged()
        {
            if (enableLog)
            {
                AddScriptCompilationSymbol("WS_LOG_ENABLED");
            }
            else
            {
                RemoveScriptCompilationSymbol("WS_LOG_ENABLED");
            }
        }

        private void RemoveScriptCompilationSymbol(string symbol)
        {
            NamedBuildTarget namedBuildTarget = GetCurrentNamedBuildTarget();
            string symbols = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
            var symbolList = symbols.Split(';');
            symbols = string.Join(";", Array.FindAll(symbolList, item => item != symbol && !string.IsNullOrEmpty(item)));
            PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, symbols);
        }

        private void AddScriptCompilationSymbol(string symbol)
        {
            NamedBuildTarget namedBuildTarget = GetCurrentNamedBuildTarget();
            string symbols = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
            if (!symbols.Contains(symbol))
            {
                PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget,
                    string.IsNullOrEmpty(symbols) ? symbol : symbols + ";" + symbol);
            }
        }

        private static NamedBuildTarget GetCurrentNamedBuildTarget()
        {
            BuildTargetGroup currentPlatform = EditorUserBuildSettings.selectedBuildTargetGroup;
            return NamedBuildTarget.FromBuildTargetGroup(currentPlatform);
        }
#endif
    }
}