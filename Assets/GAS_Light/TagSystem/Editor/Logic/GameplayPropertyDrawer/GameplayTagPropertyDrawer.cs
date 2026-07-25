#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using WS_Modules.GAS.TAG;

namespace WSFrame.GAS.Editor
{
    /// <summary>在 Inspector 中以层级下拉树选择已烘焙 Gameplay Tag，并仅序列化 TagId。</summary>
    [CustomPropertyDrawer(typeof(GameplayTag))]
    public sealed class GameplayTagPropertyDrawer : PropertyDrawer
    {
        /// <summary>绘制 Tag 选择按钮和数据库歧义提示。</summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty idProperty = property.FindPropertyRelative("id");
            if (idProperty == null)
            {
                EditorGUI.HelpBox(position, "GameplayTag.id 无法序列化。", MessageType.Error);
                return;
            }

            Rect valueRect = EditorGUI.PrefixLabel(position, label);
            GameplayTagDatabase database = ResolveDatabase(out string error);
            string display = BuildDisplay(database, new GameplayTag(idProperty.intValue), error);
            if (!EditorGUI.DropdownButton(valueRect, new GUIContent(display), FocusType.Keyboard)) return;
            if (database == null)
            {
                GAS_SettingWindow.ShowWindow();
                return;
            }

            var dropdown = new GameplayTagAdvancedDropdown(new AdvancedDropdownState(), database, selectedId =>
            {
                idProperty.intValue = selectedId;
                idProperty.serializedObject.ApplyModifiedProperties();
            });
            dropdown.Show(valueRect);
        }

        // 优先会话数据库；否则仅在项目恰好存在一个数据库时自动使用。
        private static GameplayTagDatabase ResolveDatabase(out string error)
        {
            GameplayTagDatabase sessionDatabase = GameplayTagEditorSession.GetDatabase();
            if (sessionDatabase != null)
            {
                error = string.Empty;
                return sessionDatabase;
            }

            string[] guids = AssetDatabase.FindAssets("t:GameplayTagDatabase");
            if (guids.Length == 1)
            {
                error = string.Empty;
                return AssetDatabase.LoadAssetAtPath<GameplayTagDatabase>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            error = guids.Length == 0 ? "未找到 GameplayTagDatabase（点击打开窗口）" : "存在多个数据库，请先在 Tag 窗口选择";
            return null;
        }

        // 使用当前作者路径显示已烘焙标签，废弃或未知 ID 明确标记为失效。
        private static string BuildDisplay(GameplayTagDatabase database, GameplayTag tag, string error)
        {
            if (database == null) return error;
            if (!tag.IsValid) return "None";
            return database.TryGetBakedPath(tag, out string path) ? path : $"Invalid TagId ({tag.Id})";
        }

        /// <summary>构建支持任意深度路径的 AdvancedDropdown。</summary>
        private sealed class GameplayTagAdvancedDropdown : AdvancedDropdown
        {
            private readonly GameplayTagDatabase database;
            private readonly Action<int> onSelected;
            private readonly Dictionary<int, int> dropdownIdToTagId = new();
            private int nextDropdownId = 1;

            /// <summary>创建标签层级下拉框。</summary>
            public GameplayTagAdvancedDropdown(AdvancedDropdownState state, GameplayTagDatabase database,
                Action<int> onSelected)
                : base(state)
            {
                this.database = database;
                this.onSelected = onSelected;
                minimumSize = new Vector2(280f, 320f);
            }

            // 从已烘焙作者节点构造树；新建但未 Bake 的节点不会进入列表。
            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("Gameplay Tags");
                var itemByPath = new Dictionary<string, AdvancedDropdownItem>(StringComparer.Ordinal);
                foreach (GameplayTagEditorNode node in database.EditorNodes.OrderBy(node => GetPath(node),
                             StringComparer.Ordinal))
                {
                    if (!database.TryGetBakedTag(node.Guid, out GameplayTag tag)) continue;
                    string path = GetPath(node);
                    string[] segments = path.Split('.');
                    string currentPath = string.Empty;
                    AdvancedDropdownItem parent = root;
                    for (int i = 0; i < segments.Length; i++)
                    {
                        currentPath = i == 0 ? segments[i] : currentPath + "." + segments[i];
                        if (!itemByPath.TryGetValue(currentPath, out AdvancedDropdownItem item))
                        {
                            item = new AdvancedDropdownItem(segments[i]) { id = nextDropdownId++ };
                            itemByPath.Add(currentPath, item);
                            parent.AddChild(item);
                        }

                        parent = item;
                    }

                    dropdownIdToTagId[parent.id] = tag.Id;
                }

                return root;
            }

            /// <summary>把下拉项映射回稳定 TagId。</summary>
            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (dropdownIdToTagId.TryGetValue(item.id, out int tagId)) onSelected(tagId);
            }

            // 仅在 Editor 中按父链计算显示路径，不写入运行时数据。
            private string GetPath(GameplayTagEditorNode node)
            {
                var segments = new Stack<string>();
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (node != null && visited.Add(node.Guid))
                {
                    segments.Push(node.Name);
                    string parentGuid = node.ParentGuid;
                    node = string.IsNullOrEmpty(parentGuid)
                        ? null
                        : database.EditorNodes.FirstOrDefault(candidate =>
                            candidate != null && candidate.Guid == parentGuid);
                }

                return string.Join(".", segments);
            }
        }
    }
}
#endif