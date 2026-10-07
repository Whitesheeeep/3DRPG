using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;

namespace WS_Modules
{
    /// <summary>集中处理场景配置与任务资产的重命名、创建、复制和单资产回收站操作。</summary>
    internal sealed class SceneLoadAssetService
    {
        #region 任务资产重命名

        // Windows 会把这些设备名当作特殊文件目标，即使它们没有非法字符。
        private static readonly HashSet<string> ReservedWindowsFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>通过 AssetDatabase 同步任务资产文件名和 ScriptableObject 名称，并保留资产 GUID。</summary>
        /// <param name="task">已经保存到 Assets 中的任务资产。</param>
        /// <param name="requestedName">用户输入的新名称。</param>
        /// <returns>重命名后的 Unity 工程相对路径。</returns>
        /// <exception cref="ArgumentNullException">任务资产为空。</exception>
        /// <exception cref="ArgumentException">名称为空或不能作为文件名使用。</exception>
        /// <exception cref="InvalidOperationException">任务尚未保存为 .asset，目标名称已被占用或 Unity 重命名失败。</exception>
        public string RenameTaskAsset(SceneLoadTask task, string requestedName)
        {
            if (task == null)
            {
                LogRenameFailure(null, "任务资产引用为空");
                throw new ArgumentNullException(nameof(task));
            }
            if (string.IsNullOrWhiteSpace(requestedName))
            {
                LogRenameFailure(task, "输入名称为空");
                throw new ArgumentException("任务名称不能为空。", nameof(requestedName));
            }

            string normalizedName = requestedName.Trim();
            ValidateTaskAssetName(task, normalizedName);

            string currentPath = AssetDatabase.GetAssetPath(task);
            if (string.IsNullOrEmpty(currentPath) || !currentPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                LogRenameFailure(task, "任务未保存到 Assets 文件夹");
                throw new InvalidOperationException("任务必须是已保存到 Assets 文件夹中的资产，才能重命名。");
            }

            string extension = Path.GetExtension(currentPath);
            if (!string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase))
            {
                LogRenameFailure(task, $"任务资产扩展名不是 .asset，assetPath={currentPath}");
                throw new InvalidOperationException($"任务资产扩展名不是 .asset，无法安全重命名：{currentPath}");
            }

            string assetDirectory = Path.GetDirectoryName(currentPath)?.Replace('\\', '/');
            string targetPath = $"{assetDirectory}/{normalizedName}{extension}";
            bool sameFileIgnoringCase = string.Equals(currentPath, targetPath, StringComparison.OrdinalIgnoreCase);
            string projectDirectory = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectDirectory))
            {
                LogRenameFailure(task, "无法解析 Unity 项目目录");
                throw new InvalidOperationException("无法解析 Unity 项目目录，任务资产未重命名。");
            }
            string absoluteTargetPath = Path.Combine(projectDirectory,
                targetPath.Replace('/', Path.DirectorySeparatorChar));
            if (!sameFileIgnoringCase &&
                (File.Exists(absoluteTargetPath) || AssetDatabase.LoadMainAssetAtPath(targetPath) != null))
            {
                LogRenameFailure(task, $"目标路径已存在，targetPath={targetPath}");
                throw new InvalidOperationException($"同目录中已存在名为“{normalizedName}”的资产。");
            }

            string previousName = task.name;
            if (!string.Equals(currentPath, targetPath, StringComparison.Ordinal))
            {
                // AssetDatabase 重命名文件而不重建资产，因此共享引用继续使用原 GUID。
                string renameError = AssetDatabase.RenameAsset(currentPath, normalizedName);
                if (!string.IsNullOrEmpty(renameError))
                {
                    LogRenameFailure(task, $"AssetDatabase.RenameAsset 失败，assetPath={currentPath}，reason={renameError}");
                    throw new InvalidOperationException($"任务资产重命名失败：{renameError}");
                }
            }

            task.name = normalizedName;
            EditorUtility.SetDirty(task);
            AssetDatabase.SaveAssets();
            string renamedPath = AssetDatabase.GetAssetPath(task);
            WSLog.Log($"[SceneLoadAssetService] 已同步任务资产名称，oldName={previousName}，newName={normalizedName}，assetPath={renamedPath}。");
            return renamedPath;
        }

        /// <summary>拒绝无法作为 Windows 项目文件名的任务名称，并记录用户输入校验失败。</summary>
        /// <param name="task">正在重命名的任务资产。</param>
        /// <param name="name">已去除首尾空白的名称。</param>
        /// <exception cref="ArgumentException">名称包含非法字符、以句点结尾或使用系统保留名。</exception>
        private static void ValidateTaskAssetName(SceneLoadTask task, string name)
        {
            if (name.Length > 255 || name == "." || name == ".." || name.EndsWith(".", StringComparison.Ordinal) ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0)
            {
                const string invalidNameReason = "任务名称过长，或包含非法文件名字符、以句点结尾";
                LogRenameFailure(task, invalidNameReason);
                throw new ArgumentException(invalidNameReason + "。", nameof(name));
            }

            string deviceName = Path.GetFileNameWithoutExtension(name).TrimEnd(' ', '.');
            if (ReservedWindowsFileNames.Contains(deviceName))
            {
                string reservedNameReason = $"“{deviceName}”是 Windows 保留文件名，不能用作任务名称";
                LogRenameFailure(task, reservedNameReason);
                throw new ArgumentException(reservedNameReason + "。", nameof(name));
            }
        }

        /// <summary>记录任务资产重命名失败原因，供 Editor Console 诊断。</summary>
        /// <param name="task">当前任务资产；为空时表示调用方没有提供资产。</param>
        /// <param name="reason">没有修改资产时的失败原因。</param>
        private static void LogRenameFailure(SceneLoadTask task, string reason)
        {
            string taskName = task == null ? "<null>" : task.name;
            WSLog.LogError($"[SceneLoadAssetService] 任务资产重命名失败，task={taskName}，reason={reason}；资产未修改。");
        }

        #endregion

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
