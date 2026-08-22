#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WS_Modules.Utilities.Editor
{
    /// <summary>
    /// 管理由 PhysicsShapeData Inspector 按钮显式开启的单字段临时 Handle 编辑会话。
    /// </summary>
    internal static class PhysicsShapeHandleEditor
    {
        #region 状态

        // 单字段会话只缓存用户明确点击编辑的目标，避免空闲时扫描对象或创建 SerializedObject。
        private static Component activeTarget;
        private static string activePropertyPath;
        private static SerializedObject activeSerializedObject;
        private static bool sceneGuiRegistered;
        private static bool undoRegistered;
        private static readonly Color HandleColor = new(0.2f, 0.85f, 1f, 0.9f);

        #endregion

        #region Inspector 操作

        /// <summary>
        /// 判断指定属性当前是否处于临时 Handle 编辑会话中。
        /// </summary>
        /// <param name="target">包含形状数据的宿主对象。</param>
        /// <param name="propertyPath">形状数据的 SerializedProperty 路径。</param>
        /// <returns>当前字段正在编辑时返回 true。</returns>
        internal static bool IsEditing(UnityEngine.Object target, string propertyPath)
        {
            if (!HasValidSession() || target == null || string.IsNullOrEmpty(propertyPath)) return false;
            return activeTarget.GetInstanceID() == target.GetInstanceID() &&
                activePropertyPath == propertyPath;
        }

        /// <summary>
        /// 开始或停止指定形状字段的单字段临时 Handle 编辑会话。
        /// </summary>
        /// <param name="target">包含形状数据的宿主组件。</param>
        /// <param name="propertyPath">形状数据的 SerializedProperty 路径。</param>
        internal static void Toggle(Component target, string propertyPath)
        {
            if (target == null || string.IsNullOrEmpty(propertyPath)) return;
            if (IsEditing(target, propertyPath))
            {
                StopEditing();
                return;
            }

            // 新字段开始编辑时替换旧会话，保证 SceneView 只处理一个明确目标。
            StopEditing();
            SerializedObject serializedObject = new(target);
            serializedObject.Update();
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (!IsShapeProperty(property))
            {
                serializedObject.Dispose();
                return;
            }

            activeTarget = target;
            activePropertyPath = propertyPath;
            activeSerializedObject = serializedObject;
            RegisterSessionEvents();
            // Inspector 按钮结束后把键盘焦点交给 SceneView，确保 Handle 和 Esc 可立即响应。
            EditorApplication.delayCall += FocusSceneView;
            SceneView.RepaintAll();
        }

        #endregion

        #region Scene 编辑

        /// <summary>
        /// 处理当前单字段会话的 Scene Handle 绘制和序列化写回。
        /// </summary>
        /// <param name="sceneView">当前接收 Scene GUI 的视图。</param>
        private static void OnSceneGui(SceneView sceneView)
        {
            if (!HasValidSession())
            {
                StopEditing();
                return;
            }

            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
            {
                // Esc 只结束临时编辑会话，不修改 CanDrawGizmos 等持久化数据。
                StopEditing();
                currentEvent.Use();
                return;
            }

            activeSerializedObject.UpdateIfRequiredOrScript();
            SerializedProperty property = activeSerializedObject.FindProperty(activePropertyPath);
            if (!IsShapeProperty(property))
            {
                StopEditing();
                return;
            }

            PhysicsShapeType type = (PhysicsShapeType)property.FindPropertyRelative("type").enumValueIndex;
            if (type == PhysicsShapeType.None) return;
            if (!PhysicsShapeSceneDrawers.DrawHandles(activeTarget.transform, property, type,
                    HandleColor, Tools.current)) return;

            // Handle 修改只写回当前缓存对象，并保留 Unity Undo 与 Prefab Override 记录。
            Undo.RecordObject(activeTarget, "编辑 Physics 形状");
            activeSerializedObject.ApplyModifiedProperties();
            if (PrefabUtility.IsPartOfPrefabInstance(activeTarget))
                PrefabUtility.RecordPrefabInstancePropertyModifications(activeTarget);
            sceneView.Repaint();
        }

        #endregion

        #region 会话生命周期

        /// <summary>注册当前会话需要的 SceneView 和 Undo 事件。</summary>
        private static void RegisterSessionEvents()
        {
            if (!sceneGuiRegistered)
            {
                SceneView.duringSceneGui += OnSceneGui;
                sceneGuiRegistered = true;
            }

            if (!undoRegistered)
            {
                Undo.undoRedoPerformed += OnUndoRedo;
                undoRegistered = true;
            }
        }

        /// <summary>停止当前会话并退订所有临时事件。</summary>
        private static void StopEditing()
        {
            bool hadSession = activeTarget != null || activeSerializedObject != null ||
                sceneGuiRegistered || undoRegistered;
            // 取消尚未执行的焦点切换回调，避免停止后旧会话再次抢占窗口焦点。
            EditorApplication.delayCall -= FocusSceneView;
            if (sceneGuiRegistered)
            {
                SceneView.duringSceneGui -= OnSceneGui;
                sceneGuiRegistered = false;
            }

            if (undoRegistered)
            {
                Undo.undoRedoPerformed -= OnUndoRedo;
                undoRegistered = false;
            }

            activeSerializedObject?.Dispose();
            activeSerializedObject = null;
            activeTarget = null;
            activePropertyPath = null;
            if (hadSession) SceneView.RepaintAll();
        }

        /// <summary>把最近使用的 SceneView 设为临时编辑会话的输入焦点。</summary>
        private static void FocusSceneView()
        {
            if (!HasValidSession()) return;
            SceneView.lastActiveSceneView?.Focus();
        }

        /// <summary>检查目标组件、属性路径和 SerializedObject 是否仍可用于编辑。</summary>
        /// <returns>会话仍可继续处理时返回 true。</returns>
        private static bool HasValidSession()
        {
            return activeTarget != null && activeSerializedObject != null &&
                activeSerializedObject.targetObject != null && !string.IsNullOrEmpty(activePropertyPath);
        }

        /// <summary>Undo 或 Redo 后刷新当前会话的序列化数据和 Scene 绘制。</summary>
        private static void OnUndoRedo()
        {
            if (!HasValidSession())
            {
                StopEditing();
                return;
            }

            activeSerializedObject.Update();
            SceneView.RepaintAll();
        }

        #endregion

        #region 数据校验

        /// <summary>判断属性是否为通用 PhysicsShapeData 根属性。</summary>
        /// <param name="property">待检查的序列化属性。</param>
        /// <returns>符合 PhysicsShapeData 结构时返回 true。</returns>
        private static bool IsShapeProperty(SerializedProperty property)
        {
            if (property == null || (property.propertyType != SerializedPropertyType.Generic &&
                property.propertyType != SerializedPropertyType.ManagedReference)) return false;
            return property.FindPropertyRelative("type")?.propertyType == SerializedPropertyType.Enum &&
                property.FindPropertyRelative("localPosition")?.propertyType == SerializedPropertyType.Vector3;
        }

        #endregion
    }
}
#endif
