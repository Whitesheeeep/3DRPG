using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;

namespace WS_Modules
{
    /// <summary>集中处理场景配置与任务资产的创建、复制和单资产回收站操作。</summary>
    internal sealed class SceneLoadAssetService
    {
        #region 资产目录

        /// <summary>获取新建配置和任务资产使用的默认目录。</summary>
        public string AssetFolder { get; set; } = "Assets/SceneLoadAssets";

        /// <summary>确保用户指定的项目内资产目录存在。</summary>
        /// <param name="relativePath">从 Assets 开始的项目相对目录。</param>
        public void EnsureFolder(string relativePath)
        {
            string normalizedPath = NormalizeAssetFolder(relativePath);
            string[] segments = normalizedPath.Split('/');
            string currentPath = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string nextPath = currentPath + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(nextPath))
                    AssetDatabase.CreateFolder(currentPath, segments[index]);
                currentPath = nextPath;
            }
        }

        /// <summary>将文件选择结果规范为 Assets 下的可写项目路径。</summary>
        /// <param name="relativePath">用户输入的目录。</param>
        /// <returns>规范化的 Assets 相对目录。</returns>
        public string NormalizeAssetFolder(string relativePath)
        {
            string candidate = string.IsNullOrWhiteSpace(relativePath)
                ? "Assets/SceneLoadAssets"
                : relativePath.Replace('\\', '/').TrimEnd('/');
            string[] pathSegments = candidate.Split('/');
            if (pathSegments.Length == 0 || !string.Equals(pathSegments[0], "Assets", StringComparison.Ordinal) ||
                Array.Exists(pathSegments, segment => string.IsNullOrWhiteSpace(segment) || segment == "." || segment == ".."))
                throw new InvalidOperationException("新建资产目录必须位于 Unity 项目的 Assets 文件夹中。");
            return string.Join("/", pathSegments);
        }

        #endregion

        #region 资产创建复制

        // 新资产通过 Undo 注册，再由 AssetDatabase 保存到项目目录。
        /// <summary>创建并保存一个具有唯一文件名的 ScriptableObject 资产。</summary>
        /// <typeparam name="TAsset">资产类型。</typeparam>
        /// <param name="folder">资产保存目录。</param>
        /// <param name="fileName">期望文件名。</param>
        /// <returns>已经保存到项目的资产实例。</returns>
        public TAsset CreateAsset<TAsset>(string folder, string fileName) where TAsset : ScriptableObject
        {
            EnsureFolder(folder);
            TAsset asset = ScriptableObject.CreateInstance<TAsset>();
            Undo.RegisterCreatedObjectUndo(asset, "创建场景加载资产");
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}.asset"));
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>按运行时类型创建一个 SceneLoadTask 资产。</summary>
        /// <param name="taskType">SceneLoadTask 的具体派生类型。</param>
        /// <param name="fileName">期望文件名。</param>
        /// <returns>已经保存到 Tasks 目录的任务资产。</returns>
        public SceneLoadTask CreateTaskAsset(Type taskType, string fileName)
        {
            if (taskType == null || taskType.IsAbstract || !typeof(SceneLoadTask).IsAssignableFrom(taskType))
                throw new ArgumentException("任务类型必须是可实例化的 SceneLoadTask 派生类型。", nameof(taskType));
            string folder = AssetFolder + "/Tasks";
            EnsureFolder(folder);
            SceneLoadTask task = ScriptableObject.CreateInstance(taskType) as SceneLoadTask;
            Undo.RegisterCreatedObjectUndo(task, "创建场景加载任务");
            AssetDatabase.CreateAsset(task, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}.asset"));
            EditorUtility.SetDirty(task);
            return task;
        }

        // 任务子树复制使用单次源到副本映射，维持子树内部共享。
        /// <summary>复制任务子树，并在副本内部保留同源资产的共享关系。</summary>
        /// <param name="source">需要复制的任务根节点。</param>
        /// <returns>复制后独立保存的任务根节点。</returns>
        public SceneLoadTask CloneTaskSubtree(SceneLoadTask source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var cloneBySourceMap = new Dictionary<SceneLoadTask, SceneLoadTask>();
            return CloneTaskRecursive(source, cloneBySourceMap);
        }

        // 配置复制建立新的任务资产树，并重新生成稳定 SceneId。
        /// <summary>复制场景配置及其完整任务树，保留原配置不变。</summary>
        /// <param name="source">需要复制的场景配置。</param>
        /// <returns>指向新任务资产树的场景配置副本。</returns>
        public SceneLoadConfig CloneConfig(SceneLoadConfig source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            string folder = AssetFolder;
            EnsureFolder(folder);
            SceneLoadConfig clone = ScriptableObject.CreateInstance<SceneLoadConfig>();
            EditorUtility.CopySerialized(source, clone);
            Undo.RegisterCreatedObjectUndo(clone, "复制场景配置");
            AssetDatabase.CreateAsset(clone, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{source.name}_Copy.asset"));

            // 配置副本需要独立任务树，克隆映射会在子树内部复用同一个新资产。
            var cloneBySourceMap = new Dictionary<SceneLoadTask, SceneLoadTask>();
            SceneLoadTask rootClone = source.RootTask == null ? null : CloneTaskRecursive(source.RootTask, cloneBySourceMap);
            SerializedObject serializedClone = new SerializedObject(clone);
            serializedClone.FindProperty("rootTask").objectReferenceValue = rootClone;
            serializedClone.ApplyModifiedPropertiesWithoutUndo();
            serializedClone.Update();
            string cloneGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clone));
            string copiedSceneId = string.IsNullOrWhiteSpace(source.SceneId)
                ? "scene.copy." + cloneGuid.Substring(0, Math.Min(8, cloneGuid.Length))
                : source.SceneId + ".copy." + cloneGuid.Substring(0, Math.Min(8, cloneGuid.Length));
            serializedClone.FindProperty("sceneId").stringValue = copiedSceneId;
            serializedClone.FindProperty("displayName").stringValue = source.DisplayName + " Copy";
            serializedClone.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(clone);
            return clone;
        }

        // TypeCache 避免编辑器维护一份与运行时任务类型重复的清单。
        /// <summary>查找所有可被场景编排器编辑的任务资产类型。</summary>
        /// <returns>可实例化的场景加载任务类型列表。</returns>
        public IReadOnlyList<Type> GetCreatableTaskTypes()
        {
            var types = new List<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<SceneLoadTask>())
            {
                if (!type.IsAbstract && !type.ContainsGenericParameters &&
                    typeof(ScriptableObject).IsAssignableFrom(type))
                    types.Add(type);
            }
            types.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
            return types;
        }

        /// <summary>递归克隆任务资产，并复用当前子树中的共享引用副本。</summary>
        /// <param name="source">源任务资产。</param>
        /// <param name="cloneBySourceMap">源资产到新资产的一对一映射。</param>
        /// <returns>目标任务资产副本。</returns>
        private SceneLoadTask CloneTaskRecursive(
            SceneLoadTask source,
            Dictionary<SceneLoadTask, SceneLoadTask> cloneBySourceMap)
        {
            if (cloneBySourceMap.TryGetValue(source, out SceneLoadTask existingClone)) return existingClone;

            string folder = AssetFolder + "/Tasks";
            EnsureFolder(folder);
            SceneLoadTask clone = ScriptableObject.CreateInstance(source.GetType()) as SceneLoadTask;
            EditorUtility.CopySerialized(source, clone);
            Undo.RegisterCreatedObjectUndo(clone, "复制场景任务");
            AssetDatabase.CreateAsset(clone, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{source.name}_Copy.asset"));
            cloneBySourceMap.Add(source, clone);

            if (TryGetChildren(source, out List<SceneLoadTask> children))
            {
                SerializedObject serializedClone = new SerializedObject(clone);
                SerializedProperty clonedChildren = serializedClone.FindProperty("children");
                for (int index = 0; index < children.Count; index++)
                    clonedChildren.GetArrayElementAtIndex(index).objectReferenceValue =
                        children[index] == null ? null : CloneTaskRecursive(children[index], cloneBySourceMap);
                serializedClone.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(clone);
            return clone;
        }

        #endregion

        #region 组合结构读取

        /// <summary>读取组合资产序列化的 children 列表。</summary>
        /// <param name="task">待查询任务。</param>
        /// <param name="children">组合子节点快照。</param>
        /// <returns>任务具有组合列表时返回 true。</returns>
        private static bool TryGetChildren(SceneLoadTask task, out List<SceneLoadTask> children)
        {
            children = null;
            SerializedObject serializedTask = new SerializedObject(task);
            SerializedProperty childrenProperty = serializedTask.FindProperty("children");
            if (childrenProperty == null || !childrenProperty.isArray) return false;
            children = new List<SceneLoadTask>(childrenProperty.arraySize);
            for (int index = 0; index < childrenProperty.arraySize; index++)
                children.Add(childrenProperty.GetArrayElementAtIndex(index).objectReferenceValue as SceneLoadTask);
            return true;
        }

        #endregion
    }
}
