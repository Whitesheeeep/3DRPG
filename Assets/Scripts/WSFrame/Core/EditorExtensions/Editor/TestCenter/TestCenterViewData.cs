using System;
using UnityEditor;
using UnityEngine;

namespace WS_Modules.EditorExtensions
{
    internal sealed class TesterViewData
    {
        public TesterViewData(Type testerType, MonoScript script, string scriptPath, MonoBehaviour instance)
        {
            TesterType = testerType;
            Script = script;
            ScriptPath = scriptPath;
            Instance = instance;
        }

        public Type TesterType { get; }
        public MonoScript Script { get; }
        public string ScriptPath { get; }
        public MonoBehaviour Instance { get; private set; }
        public string TypeName => TesterType?.Name ?? "Missing Type";
        public string NamespaceName => string.IsNullOrEmpty(TesterType?.Namespace) ? "全局命名空间" : TesterType.Namespace;
        public string StatusText => IsLoaded ? "已加载" : "未加载";
        public bool IsLoaded => Instance != null;
        public GameObject InstanceGameObject => Instance == null ? null : Instance.gameObject;

        public void SetInstance(MonoBehaviour instance)
        {
            Instance = instance;
        }
    }
}
