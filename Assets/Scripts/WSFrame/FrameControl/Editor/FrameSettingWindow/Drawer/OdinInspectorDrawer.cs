using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace WS_Modules
{
    internal sealed class OdinInspectorDrawer : IInspectorDrawer
    {
        public void Draw(object target, VisualElement container)
        {
            if (target == null) return;

            PropertyTree odinInspectorTree;
            SerializedObject serializedObject = null;

            if (target is Object unityObj)
            {
                serializedObject = new SerializedObject(unityObj);
                odinInspectorTree = PropertyTree.Create(serializedObject);
            }
            else
            {
                odinInspectorTree = PropertyTree.Create(target);
            }

            var imguiContainer = new IMGUIContainer(() =>
            {
                if (serializedObject != null)
                {
                    serializedObject.Update();
                }

                odinInspectorTree?.Draw();

                if (odinInspectorTree != null && odinInspectorTree.ApplyChanges())
                {
                    if (target is Object unityObject)
                    {
                        EditorUtility.SetDirty(unityObject);
                        if (!Application.isPlaying && unityObject is GameObject go)
                        {
                            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
                        }
                        else if (!Application.isPlaying && unityObject is Component comp)
                        {
                            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(comp.gameObject.scene);
                        }
                    }
                }
            });

            imguiContainer.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                odinInspectorTree?.Dispose();
                odinInspectorTree = null;
            });
            container.Add(imguiContainer);
        }

        public void DrawProperty(Object target, string propertyPath, VisualElement container)
        {
            if (target == null) return;

            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty serializedProperty = serializedObject.FindProperty(propertyPath);

            if (serializedProperty == null)
            {
                container.Add(new Label($"Property '{propertyPath}' not found."));
                return;
            }

            bool isList = IsUnityListProperty(serializedProperty);

            // 是数组则使用原生的方式进行，反之使用 Odin 进行
            if (isList)
            {
                var imguiContainer = new IMGUIContainer(() =>
                {
                    serializedObject.Update();
                    EditorGUILayout.PropertyField(serializedProperty, new GUIContent(serializedProperty.displayName), true);
                    if (serializedObject.ApplyModifiedProperties())
                    {
                        EditorUtility.SetDirty(target);
                    }
                });
                container.Add(imguiContainer);
            }
            else
            {
                PropertyTree tree = PropertyTree.Create(serializedObject);
                var targetProperty = tree.GetPropertyAtPath(propertyPath);
                if (targetProperty != null)
                {
                    ExpandRecursively(targetProperty);
                }

                var imguiContainer = new IMGUIContainer(() =>
                {
                    if (serializedObject.targetObject == null) return;

                    serializedObject.Update();

                    var property = tree.GetPropertyAtPath(propertyPath);
                    if (property != null)
                    {
                        if (property.Children == null || property.Children.Count == 0)
                        {
                            property.Draw();
                        }
                        else
                        {
                            DrawChildrenHybrid(tree, serializedProperty, propertyPath);
                        }
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(serializedProperty, true);
                    }

                    bool serializedChanged = serializedObject.ApplyModifiedProperties();
                    bool odinChanged = tree.ApplyChanges();
                    if (serializedChanged || odinChanged)
                    {
                        EditorUtility.SetDirty(target);
                    }
                });
                imguiContainer.RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    tree?.Dispose();
                    tree = null;
                });
                container.Add(imguiContainer);
            }
        }

        public void DrawUnityProperty(Object target, string propertyPath, VisualElement container)
        {
            if (target == null) return;

            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty serializedProperty = serializedObject.FindProperty(propertyPath);

            if (serializedProperty == null)
            {
                container.Add(new Label($"Property '{propertyPath}' not found."));
                return;
            }

            var imguiContainer = new IMGUIContainer(() =>
            {
                if (serializedObject.targetObject == null) return;

                serializedObject.Update();
                EditorGUILayout.PropertyField(serializedProperty, true);
                if (serializedObject.ApplyModifiedProperties())
                {
                    EditorUtility.SetDirty(target);
                }
            });
            container.Add(imguiContainer);
        }

        // List/Array 用 Unity 原生绘制以避开 Odin List 在嵌入式 IMGUI 面板中的异常，其它字段继续使用 Odin。
        private static void DrawChildrenHybrid(PropertyTree tree, SerializedProperty rootProperty, string rootPropertyPath)
        {
            var iterator = rootProperty.Copy();
            var endProperty = iterator.GetEndProperty();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, endProperty))
            {
                enterChildren = false;

                if (iterator.depth != rootProperty.depth + 1)
                {
                    continue;
                }

                if (IsUnityListProperty(iterator))
                {
                    EditorGUILayout.PropertyField(iterator, true);
                    continue;
                }

                var odinPropertyPath = $"{rootPropertyPath}.{iterator.name}";
                var odinProperty = tree.GetPropertyAtPath(odinPropertyPath);
                if (odinProperty != null)
                {
                    odinProperty.Draw();
                }
                else
                {
                    EditorGUILayout.PropertyField(iterator, true);
                }
            }
        }

        private static bool IsUnityListProperty(SerializedProperty property)
        {
            return property != null &&
                   property.isArray &&
                   property.propertyType == SerializedPropertyType.Generic;
        }


        private void ExpandRecursively(InspectorProperty property)
        {
            if (property.Children != null && property.Children.Count > 0)
            {
                property.State.Expanded = true;
                foreach (var child in property.Children)
                {
                    ExpandRecursively(child);
                }
            }
        }
    }
}

