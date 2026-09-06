#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>在 Inspector 中选择已烘焙 Gameplay Attribute，并同步序列化稳定 ID 与展示名称。</summary>
    [CustomPropertyDrawer(typeof(GameplayAttribute))]
    public sealed class GameplayAttributePropertyDrawer : PropertyDrawer
    {
        #region Inspector 绘制

        /// <summary>绘制 Attribute 下拉选择；Registry 不明确时提供进入 Attribute 编辑器的入口。</summary>
        /// <param name="position">Unity 分配给属性的绘制区域。</param>
        /// <param name="property">包含私有 id 字段的 GameplayAttribute 序列化属性。</param>
        /// <param name="label">Inspector 字段标签。</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty idProperty = property.FindPropertyRelative("id");
            SerializedProperty nameProperty = property.FindPropertyRelative("name");
            if (idProperty == null)
            {
                EditorGUI.HelpBox(position, "GameplayAttribute.id 无法序列化。", MessageType.Error);
                return;
            }

            Rect valueRect = EditorGUI.PrefixLabel(position, label);
            GameplayAttributeRegistry registry =
                GameplayAttributeEditorSession.ResolveSingleRegistry(out string error);
            string display = BuildDisplay(registry, idProperty.intValue, error);
            if (!EditorGUI.DropdownButton(valueRect, new GUIContent(display), FocusType.Keyboard)) return;
            if (registry == null)
            {
                GAS_SettingWindow.ShowGameplayAttributes(
                    null,
                    null,
                    GameplayAttributeEditorPage.Specs);
                return;
            }

            var dropdown = new GameplayAttributeAdvancedDropdown(
                new AdvancedDropdownState(),
                registry,
                selectedId =>
                {
                    idProperty.intValue = selectedId;
                    if (nameProperty != null)
                        nameProperty.stringValue = selectedId >= 0 && registry.TryGetNodeById(selectedId, out GameplayAttributeEditorNode selectedNode)
                            ? selectedNode.Name
                            : string.Empty;
                    idProperty.serializedObject.ApplyModifiedProperties();
                });
            dropdown.Show(valueRect);
        }

        // 使用当前作者名称显示有效 ID；废弃或未知 ID 保留明确诊断文本。
        private static string BuildDisplay(
            GameplayAttributeRegistry registry,
            int attributeId,
            string error)
        {
            if (registry == null) return error;
            if (attributeId < 0) return "None";
            return registry.TryGetNodeById(attributeId, out GameplayAttributeEditorNode node)
                ? node.Name
                : $"Invalid AttributeId ({attributeId})";
        }

        #endregion

        #region 下拉实现

        /// <summary>显示按名称排序的已烘焙 Attribute 平铺选择列表。</summary>
        private sealed class GameplayAttributeAdvancedDropdown : AdvancedDropdown
        {
            #region 字段

            private readonly GameplayAttributeRegistry registry;
            private readonly Action<int> onSelected;
            private readonly Dictionary<int, int> dropdownIdToAttributeId = new();
            private int nextDropdownId = 1;

            #endregion

            #region 生命周期

            /// <summary>创建一次 Attribute 下拉选择会话。</summary>
            /// <param name="state">Unity AdvancedDropdown 导航状态。</param>
            /// <param name="registry">用于枚举已烘焙 Spec 的 Registry。</param>
            /// <param name="onSelected">选择完成后的稳定 ID 回调。</param>
            public GameplayAttributeAdvancedDropdown(
                AdvancedDropdownState state,
                GameplayAttributeRegistry registry,
                Action<int> onSelected)
                : base(state)
            {
                this.registry = registry;
                this.onSelected = onSelected;
                minimumSize = new Vector2(280f, 320f);
            }

            #endregion

            #region 构建与选择

            // 每次打开都重建 ID 映射，避免 Registry 切换或域重载后复用旧选择。
            protected override AdvancedDropdownItem BuildRoot()
            {
                dropdownIdToAttributeId.Clear();
                nextDropdownId = 1;
                var root = new AdvancedDropdownItem("Gameplay Attributes");
                AddSelectableItem(root, "None", GameplayAttribute.InvalidId);

                var choices = new List<AttributeChoice>();
                for (int i = 0; i < registry.Nodes.Count; i++)
                {
                    GameplayAttributeEditorNode node = registry.Nodes[i];
                    if (node != null &&
                        registry.TryGetBakedAttribute(node.Guid, out GameplayAttribute attribute))
                        choices.Add(new AttributeChoice(node.Name, attribute.Id));
                }

                choices.Sort((left, right) =>
                    string.Compare(left.Name, right.Name, StringComparison.Ordinal));
                for (int i = 0; i < choices.Count; i++)
                    AddSelectableItem(root, choices[i].Name, choices[i].Id);
                return root;
            }

            /// <summary>把被选下拉项映射回稳定 AttributeId。</summary>
            /// <param name="item">Unity 返回的下拉项。</param>
            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (dropdownIdToAttributeId.TryGetValue(item.id, out int attributeId))
                    onSelected(attributeId);
            }

            // 创建唯一显示项，并记录本次 Dropdown 内部 ID 到 AttributeId 的映射。
            private void AddSelectableItem(
                AdvancedDropdownItem parent,
                string name,
                int attributeId)
            {
                var item = new AdvancedDropdownItem(name) { id = nextDropdownId++ };
                dropdownIdToAttributeId[item.id] = attributeId;
                parent.AddChild(item);
            }

            #endregion

            #region 内部类型

            /// <summary>用于确定排序的临时 Attribute 选择项。</summary>
            private readonly struct AttributeChoice
            {
                /// <summary>创建临时选择项。</summary>
                /// <param name="name">作者显示名称。</param>
                /// <param name="id">稳定 AttributeId。</param>
                public AttributeChoice(string name, int id)
                {
                    Name = name;
                    Id = id;
                }

                /// <summary>获取作者显示名称。</summary>
                public string Name { get; }

                /// <summary>获取稳定 AttributeId。</summary>
                public int Id { get; }
            }

            #endregion
        }

        #endregion
    }
}
#endif
