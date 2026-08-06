#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.Editor
{
    /// <summary>表示 Gameplay Tag 作者数据的校验严重程度。</summary>
    public enum GameplayTagValidationSeverity
    {
        Warning,
        Error
    }

    /// <summary>描述一个可展示并可阻止烘焙的 Gameplay Tag 校验问题。</summary>
    public readonly struct GameplayTagValidationIssue
    {
        /// <summary>获取严重程度。</summary>
        public GameplayTagValidationSeverity Severity { get; }
        /// <summary>获取关联节点 Guid。</summary>
        public string NodeGuid { get; }
        /// <summary>获取问题说明。</summary>
        public string Message { get; }

        /// <summary>创建校验问题。</summary>
        public GameplayTagValidationIssue(GameplayTagValidationSeverity severity, string nodeGuid, string message)
        {
            Severity = severity;
            NodeGuid = nodeGuid;
            Message = message;
        }

        /// <summary>返回用于列表展示的问题文本。</summary>
        public override string ToString() => $"[{Severity}] {Message}";
    }

    /// <summary>集中处理 Gameplay Tag 作者数据变更、Undo、校验和烘焙。</summary>
    public sealed class GameplayTagEditorService
    {
        #region 字段与属性
        private readonly GameplayTagDatabase database;
        /// <summary>获取当前操作的数据库。</summary>
        public GameplayTagDatabase Database => database;
        #endregion

        /// <summary>创建绑定指定数据库的 Editor 服务，并迁移旧 Odin comparer 数据。</summary>
        /// <param name="database">需要编辑的 Gameplay Tag 数据库。</param>
        public GameplayTagEditorService(GameplayTagDatabase database)
        {
            this.database = database;
            if (database.NormalizeBakedIdHistoryComparer()) SaveDatabase();
        }

        #region 作者操作
        /// <summary>添加根节点或指定节点的子节点。</summary>
        public GameplayTagEditorNode AddNode(string parentGuid, string requestedName = "NewTag")
        {
            string name = CreateUniqueSiblingName(parentGuid, requestedName);
            Undo.RecordObject(database, "Add Gameplay Tag");
            GameplayTagEditorNode node = database.CreateEditorNode(name, parentGuid);
            SaveDatabase();
            return node;
        }

        /// <summary>校验并重命名指定节点。</summary>
        public bool TryRename(GameplayTagEditorNode node, string newName, out string error)
        {
            error = ValidateName(node, newName, node?.ParentGuid);
            if (!string.IsNullOrEmpty(error)) return false;
            Undo.RecordObject(database, "Rename Gameplay Tag");
            database.RenameEditorNode(node, newName.Trim());
            SaveDatabase();
            return true;
        }

        /// <summary>按完整路径自动创建缺失父级，并原子重命名和移动当前节点。</summary>
        /// <param name="node">待修改节点。</param>
        /// <param name="requestedPath">以点分隔的完整目标路径。</param>
        /// <param name="error">失败时返回原因。</param>
        /// <returns>路径有效且完成提交时返回 true。</returns>
        public bool TrySetPath(GameplayTagEditorNode node, string requestedPath, out string error)
        {
            error = string.Empty;
            if (node == null)
            {
                error = "没有可修改的节点。";
                return false;
            }

            string[] segments = (requestedPath ?? string.Empty).Split('.').Select(segment => segment.Trim()).ToArray();
            if (segments.Length == 0 || segments.Any(string.IsNullOrEmpty))
            {
                error = "Path 不能包含空层级。";
                return false;
            }

            string parentGuid = string.Empty;
            int firstMissingIndex = -1;
            for (int index = 0; index < segments.Length - 1; index++)
            {
                GameplayTagEditorNode existing = FindChild(parentGuid, segments[index], node);
                if (existing == null)
                {
                    firstMissingIndex = index;
                    break;
                }

                if (existing == node || IsDescendant(existing.Guid, node.Guid))
                {
                    error = "目标父路径不能位于当前节点自身或其后代中。";
                    return false;
                }

                parentGuid = existing.Guid;
            }

            if (firstMissingIndex < 0)
            {
                GameplayTagEditorNode conflict = FindChild(parentGuid, segments[^1], node);
                if (conflict != null)
                {
                    error = $"目标路径已被其他 Tag 占用：{requestedPath}";
                    return false;
                }

                if (!string.IsNullOrEmpty(parentGuid) && IsDescendant(parentGuid, node.Guid))
                {
                    error = "目标父路径不能位于当前节点后代中。";
                    return false;
                }
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Set Gameplay Tag Path");
            Undo.RecordObject(database, "Set Gameplay Tag Path");
            int creationStart = firstMissingIndex < 0 ? segments.Length - 1 : firstMissingIndex;
            for (int index = creationStart; index < segments.Length - 1; index++)
            {
                GameplayTagEditorNode created = database.CreateEditorNode(segments[index], parentGuid);
                parentGuid = created.Guid;
            }

            database.RenameEditorNode(node, segments[^1]);
            database.ReparentEditorNode(node, parentGuid);
            Undo.CollapseUndoOperations(undoGroup);
            SaveDatabase();
            return true;
        }

        /// <summary>更新节点描述并记录 Undo。</summary>
        public void SetDescription(GameplayTagEditorNode node, string description)
        {
            if (node == null || node.Description == description) return;
            Undo.RecordObject(database, "Edit Gameplay Tag Description");
            database.SetEditorNodeDescription(node, description);
            SaveDatabase();
        }

        /// <summary>校验并把节点移动到新的父节点。</summary>
        public bool TryMove(GameplayTagEditorNode node, string newParentGuid, out string error)
        {
            error = string.Empty;
            if (node == null)
            {
                error = "没有可移动的节点。";
                return false;
            }

            if (node.Guid == newParentGuid || IsDescendant(newParentGuid, node.Guid))
            {
                error = "不能把节点移动到自身或自身后代。";
                return false;
            }

            if (!string.IsNullOrEmpty(newParentGuid) && FindNode(newParentGuid) == null)
            {
                error = "目标父节点不存在。";
                return false;
            }

            error = ValidateName(node, node.Name, newParentGuid);
            if (!string.IsNullOrEmpty(error)) return false;
            Undo.RecordObject(database, "Move Gameplay Tag");
            database.ReparentEditorNode(node, newParentGuid);
            SaveDatabase();
            return true;
        }

        /// <summary>级联删除节点及其全部后代。</summary>
        public int DeleteSubtree(GameplayTagEditorNode node)
        {
            if (node == null) return 0;
            HashSet<string> subtree = CollectSubtreeGuids(node.Guid);
            Undo.RecordObject(database, "Delete Gameplay Tag Subtree");
            int removed = database.RemoveEditorNodes(subtree);
            SaveDatabase();
            return removed;
        }
        #endregion

        #region 查询与校验
        /// <summary>按 Guid 查找作者节点。</summary>
        public GameplayTagEditorNode FindNode(string guid) =>
            database.EditorNodes.FirstOrDefault(node => node != null && node.Guid == guid);

        /// <summary>计算节点当前规范路径。</summary>
        public string GetPath(GameplayTagEditorNode node)
        {
            if (node == null) return string.Empty;
            var segments = new Stack<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            GameplayTagEditorNode current = node;
            while (current != null && visited.Add(current.Guid))
            {
                segments.Push(current.Name);
                current = string.IsNullOrEmpty(current.ParentGuid) ? null : FindNode(current.ParentGuid);
            }

            return string.Join(".", segments);
        }

        /// <summary>收集节点及全部后代的 Guid。</summary>
        public HashSet<string> CollectSubtreeGuids(string rootGuid)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(rootGuid);
            while (pending.Count > 0)
            {
                string guid = pending.Pop();
                if (!result.Add(guid)) continue;
                foreach (GameplayTagEditorNode child in database.EditorNodes)
                    if (child != null && child.ParentGuid == guid)
                        pending.Push(child.Guid);
            }

            return result;
        }

        /// <summary>执行完整作者数据校验。</summary>
        public List<GameplayTagValidationIssue> Validate()
        {
            var issues = new List<GameplayTagValidationIssue>();
            var guidMap = new Dictionary<string, GameplayTagEditorNode>(StringComparer.Ordinal);
            foreach (GameplayTagEditorNode node in database.EditorNodes)
            {
                if (node == null)
                {
                    issues.Add(Error(string.Empty, "作者节点列表包含空项。"));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(node.Guid)) issues.Add(Error(string.Empty, "存在空 Guid 节点。"));
                else if (!guidMap.TryAdd(node.Guid, node)) issues.Add(Error(node.Guid, $"Guid 重复：{node.Guid}"));
                if (string.IsNullOrWhiteSpace(node.Name)) issues.Add(Error(node.Guid, "Tag 名称不能为空。"));
                else if (node.Name.Contains(".")) issues.Add(Error(node.Guid, $"名称不能包含路径分隔符：{node.Name}"));
            }

            foreach (GameplayTagEditorNode node in database.EditorNodes.Where(item => item != null))
            {
                if (!string.IsNullOrEmpty(node.ParentGuid) && !guidMap.ContainsKey(node.ParentGuid))
                    issues.Add(Error(node.Guid, $"父节点不存在：{node.ParentGuid}"));
                if (HasParentCycle(node, guidMap)) issues.Add(Error(node.Guid, $"父级关系形成循环：{node.Name}"));
            }

            foreach (IGrouping<(string Parent, string Name), GameplayTagEditorNode> group in database.EditorNodes
                         .Where(n => n != null).GroupBy(n => (n.ParentGuid ?? string.Empty, n.Name ?? string.Empty)))
                if (group.Count() > 1)
                    foreach (GameplayTagEditorNode node in group)
                        issues.Add(Error(node.Guid, $"同一父节点下名称重复：{node.Name}"));
            var pathOwners = new Dictionary<string, string>(StringComparer.Ordinal);
            var identifiers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (GameplayTagEditorNode node in database.EditorNodes.Where(n => n != null))
            {
                string path = GetPath(node);
                if (!pathOwners.TryAdd(path, node.Guid)) issues.Add(Error(node.Guid, $"完整路径重复：{path}"));
                string identifier = RuntimeGameplayTagGenerator.CreateIdentifier(path);
                if (!identifiers.TryAdd(identifier, path)) issues.Add(Error(node.Guid, $"生成的 C# 标识符冲突：{identifier}"));
            }

            var usedIds = new Dictionary<int, string>();
            foreach (KeyValuePair<string, int> pair in database.BakedIdHistory)
                if (!usedIds.TryAdd(pair.Value, pair.Key))
                    issues.Add(Error(pair.Key, $"TagId {pair.Value} 被多个 Guid 占用。"));
            return issues;
        }
        #endregion

        #region 烘焙
        /// <summary>校验并烘焙运行时字典与生成代码。</summary>
        public bool TryBake(out List<GameplayTagValidationIssue> issues, out string message)
        {
            issues = Validate();
            if (issues.Any(issue => issue.Severity == GameplayTagValidationSeverity.Error))
            {
                message = $"烘焙失败，共 {issues.Count} 个校验问题。";
                return false;
            }

            var nodesByGuid = database.EditorNodes.ToDictionary(node => node.Guid, StringComparer.Ordinal);
            var history = new Dictionary<string, int>(StringComparer.Ordinal);
            var retired = new HashSet<int>(database.RetiredTagIds);
            int highestKnownId = database.BakedIdHistory.Values.Concat(database.RetiredTagIds).DefaultIfEmpty(-1).Max();
            int nextId = Math.Max(Math.Max(0, database.NextTagId), highestKnownId + 1);
            foreach (KeyValuePair<string, int> old in database.BakedIdHistory)
                if (!nodesByGuid.ContainsKey(old.Key))
                    retired.Add(old.Value);
            foreach (GameplayTagEditorNode node in database.EditorNodes)
            {
                if (database.BakedIdHistory.TryGetValue(node.Guid, out int id)) history.Add(node.Guid, id);
                else
                {
                    while (retired.Contains(nextId)) nextId++;
                    history.Add(node.Guid, nextId++);
                }
            }

            var runtimeNodes = new Dictionary<GameplayTag, GameplayTagNode>();
            var paths = new Dictionary<string, GameplayTag>(StringComparer.Ordinal);
            foreach (GameplayTagEditorNode editorNode in database.EditorNodes)
            {
                GameplayTag tag = new(history[editorNode.Guid]);
                GameplayTag parent = string.IsNullOrEmpty(editorNode.ParentGuid)
                    ? GameplayTag.Empty
                    : new GameplayTag(history[editorNode.ParentGuid]);
                var ancestors = new List<GameplayTag>();
                string parentGuid = editorNode.ParentGuid;
                while (!string.IsNullOrEmpty(parentGuid))
                {
                    ancestors.Add(new GameplayTag(history[parentGuid]));
                    parentGuid = nodesByGuid[parentGuid].ParentGuid;
                }

                runtimeNodes.Add(tag, new GameplayTagNode(tag, parent, ancestors.ToArray()));
                paths.Add(GetPath(editorNode), tag);
            }

            string source = RuntimeGameplayTagGenerator.GenerateSource(paths);
            try
            {
                RuntimeGameplayTagGenerator.WriteAtomically(source);
            }
            catch (Exception exception)
            {
                message = $"生成代码写入失败：{exception.Message}";
                return false;
            }

            Undo.RecordObject(database, "Bake Gameplay Tags");
            database.ApplyBake(runtimeNodes, history, retired.OrderBy(id => id).ToList(), nextId);
            SaveDatabase();
            AssetDatabase.ImportAsset(RuntimeGameplayTagGenerator.GeneratedAssetPath, ImportAssetOptions.ForceUpdate);
            message = $"烘焙成功：{runtimeNodes.Count} 个 Tag，下一个 ID 为 {nextId}。";
            return true;
        }
        #endregion

        #region 内部辅助
        // 检查候选父节点是否位于当前节点子树中。
        private bool IsDescendant(string candidateGuid, string ancestorGuid)
        {
            if (string.IsNullOrEmpty(candidateGuid)) return false;
            return CollectSubtreeGuids(ancestorGuid).Contains(candidateGuid);
        }

        // 校验单次重命名或移动后的同级约束。
        private string ValidateName(GameplayTagEditorNode node, string value, string parentGuid)
        {
            string name = value?.Trim();
            if (string.IsNullOrEmpty(name)) return "Tag 名称不能为空。";
            if (name.Contains(".")) return "Tag 名称不能包含 '.'。";
            bool duplicate = database.EditorNodes.Any(other =>
                other != null && other != node && other.ParentGuid == (parentGuid ?? string.Empty) &&
                other.Name == name);
            return duplicate ? $"目标父节点下已存在同名 Tag：{name}" : string.Empty;
        }

        // 在指定父级下按区分大小写的名称查找子节点，可排除当前编辑节点。
        private GameplayTagEditorNode FindChild(string parentGuid, string name, GameplayTagEditorNode excluded = null)
        {
            return database.EditorNodes.FirstOrDefault(candidate => candidate != null && candidate != excluded &&
                                                                    candidate.ParentGuid ==
                                                                    (parentGuid ?? string.Empty) &&
                                                                    candidate.Name == name);
        }

        // 为新增节点创建不会与兄弟冲突的默认名称。
        private string CreateUniqueSiblingName(string parentGuid, string baseName)
        {
            string name = baseName;
            int suffix = 1;
            while (database.EditorNodes.Any(node =>
                       node != null && node.ParentGuid == (parentGuid ?? string.Empty) && node.Name == name))
                name = $"{baseName}{suffix++}";
            return name;
        }

        // 沿父链检查循环，确保 TreeView 和烘焙遍历能够终止。
        private static bool HasParentCycle(GameplayTagEditorNode node,
            IReadOnlyDictionary<string, GameplayTagEditorNode> map)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            GameplayTagEditorNode current = node;
            while (current != null && !string.IsNullOrEmpty(current.ParentGuid))
            {
                if (!visited.Add(current.Guid)) return true;
                map.TryGetValue(current.ParentGuid, out current);
            }

            return false;
        }

        // 创建错误级别校验结果。
        private static GameplayTagValidationIssue Error(string guid, string message) =>
            new(GameplayTagValidationSeverity.Error, guid, message);

        // 标记资产并立即保存，确保 Undo/域重载后作者数据一致。
        private void SaveDatabase()
        {
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssetIfDirty(database);
        }
        #endregion
    }
}
#endif