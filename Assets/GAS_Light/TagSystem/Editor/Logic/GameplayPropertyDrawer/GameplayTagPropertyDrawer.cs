#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.Editor
{
    /// <summary>在 Inspector 中以层级下拉树选择已烘焙 Gameplay Tag，并仅序列化 TagId。</summary>
    [CustomPropertyDrawer(typeof(GameplayTag))]
    public sealed class GameplayTagPropertyDrawer : PropertyDrawer
    {
        #region Inspector 绘制

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
                GAS_SettingWindow.ShowGameplayTags(null);
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

        #endregion

        #region 层级下拉框

        /// <summary>构建支持任意深度路径的 AdvancedDropdown。</summary>
        private sealed class GameplayTagAdvancedDropdown : AdvancedDropdown
        {
            #region 字段

            private readonly GameplayTagDatabase database;
            private readonly Action<int> onSelected;
            private readonly Dictionary<int, int> dropdownIdToTagId = new();
            private int nextDropdownId = 1;

            #endregion

            #region 生命周期

            /// <summary>创建标签层级下拉框。</summary>
            public GameplayTagAdvancedDropdown(AdvancedDropdownState state, GameplayTagDatabase database,
                Action<int> onSelected)
                : base(state)
            {
                this.database = database;
                this.onSelected = onSelected;
                minimumSize = new Vector2(280f, 320f);
            }

            #endregion

            #region 下拉树构建

            // 先构造临时层级，再生成下拉项，以便区分可直接选择的叶子和负责导航的中间节点。
            protected override AdvancedDropdownItem BuildRoot()
            {
                dropdownIdToTagId.Clear();
                nextDropdownId = 1;

                var root = new AdvancedDropdownItem("Gameplay Tags");

                // 构建临时层级节点，按路径分段组织；中间节点不直接绑定 TagId。
                var modelRoot = new DropdownNode(string.Empty);
                foreach (GameplayTagEditorNode node in database.EditorNodes)
                {
                    if (node == null || !database.TryGetBakedTag(node.Guid, out GameplayTag tag)) continue;

                    string path = GetPath(node);
                    if (string.IsNullOrEmpty(path)) continue;

                    string[] segments = path.Split('.');
                    DropdownNode current = modelRoot;
                    for (int i = 0; i < segments.Length; i++)
                    {
                        if (!current.Children.TryGetValue(segments[i], out DropdownNode child))
                        {
                            child = new DropdownNode(segments[i]);
                            current.Children.Add(segments[i], child);
                        }

                        current = child;
                    }

                    current.TagId = tag.Id;
                }

                foreach (DropdownNode child in modelRoot.Children.Values)
                    root.AddChild(BuildDropdownItem(child));

                return root;
            }

            // 构建下拉项，使用 前序遍历
            // 递归生成 Dropdown；中间 Tag 使用额外叶子项选择自身，避免父项点击只执行导航。
            private AdvancedDropdownItem BuildDropdownItem(DropdownNode node)
            {
                var item = new AdvancedDropdownItem(node.Name) { id = NextDropdownId() };
                bool hasChildren = node.Children.Count > 0;

                // 叶子节点
                if (node.TagId.HasValue && !hasChildren)
                {
                    dropdownIdToTagId[item.id] = node.TagId.Value;
                    return item;
                }

                if (node.TagId.HasValue)
                {
                    var selectSelf = new AdvancedDropdownItem($"Select This Tag ({node.Name})")
                    {
                        id = NextDropdownId()
                    };
                    dropdownIdToTagId[selectSelf.id] = node.TagId.Value;
                    item.AddChild(selectSelf);
                }

                foreach (DropdownNode child in node.Children.Values)
                    item.AddChild(BuildDropdownItem(child));

                return item;
            }

            // 为本次 BuildRoot 分配唯一的下拉项 ID。
            private int NextDropdownId() => nextDropdownId++;

            #endregion

            #region 选择处理

            /// <summary>把下拉项映射回稳定 TagId。</summary>
            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (dropdownIdToTagId.TryGetValue(item.id, out int tagId)) onSelected(tagId);
            }

            #endregion

            #region 路径查询与内部模型

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

            /// <summary>表示生成 AdvancedDropdown 前使用的临时标签层级节点。</summary>
            private sealed class DropdownNode
            {
                /// <summary>创建临时层级节点。</summary>
                /// <param name="name">当前层级的局部名称。</param>
                public DropdownNode(string name)
                {
                    Name = name;
                }

                /// <summary>获取当前层级的局部名称。</summary>
                public string Name { get; }

                /// <summary>获取按名称稳定排序的子节点。</summary>
                public SortedDictionary<string, DropdownNode> Children { get; } =
                    new(StringComparer.Ordinal);

                /// <summary>获取或设置当前作者节点已烘焙的稳定 TagId；路径占位节点没有该值。</summary>
                public int? TagId { get; set; }
            }

            #endregion
        }

        #endregion
    }
}
#endif
