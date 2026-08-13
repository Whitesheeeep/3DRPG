using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace WS_Modules.Pooling.Editor
{
    /// <summary>
    /// 在编辑器域加载完成后执行各运行时程序集生成的 Class Pool 注册入口。
    /// </summary>
    [InitializeOnLoad]
    internal static class ClassPoolPrewarmRegistryEditorBootstrap
    {
        #region 初始化

        /// <summary>
        /// 安排延迟注册，确保 Unity 完成当前程序集域的初始化后再填充 Inspector 数据源。
        /// </summary>
        static ClassPoolPrewarmRegistryEditorBootstrap()
        {
            EditorApplication.delayCall += RegisterGeneratedEntries;
        }

        #endregion

        #region 注册执行

        /// <summary>
        /// 查找并执行所有带 Registrar 标记的静态无参生成方法，将生成的注册项添加到 Class Pool 中。
        /// </summary>
        private static void RegisterGeneratedEntries()
        {
            foreach (MethodInfo method in TypeCache.GetMethodsWithAttribute<ClassPoolPrewarmRegistrarAttribute>())
            {
                if (!method.IsStatic || method.GetParameters().Length != 0)
                {
                    Debug.LogError($"Invalid class pool prewarm registrar: {method.DeclaringType?.FullName}.{method.Name}.");
                    continue;
                }

                try
                {
                    // 生成方法自身保持幂等；Editor 与 Play Mode 初始化重复执行时不会产生重复条目。
                    method.Invoke(null, null);
                }
                catch (TargetInvocationException exception)
                {
                    Exception cause = exception.InnerException ?? exception;
                    Debug.LogException(cause);
                }
            }
        }

        #endregion
    }
}
