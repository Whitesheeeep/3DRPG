using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WS_Modules
{
    /// <summary>
    /// WSSceneAttribute 的编辑器绘制器，用于从 Build Settings 中选择当前启用的可加载场景。
    /// </summary>
    [CustomPropertyDrawer(typeof(WSSceneAttribute))]
    internal sealed class WSSceneDrawer : PropertyDrawer
    {
        private const float VerticalSpacing = 2f;
        private const string UnsupportedTypeMessage = "[WSScene] only supports string or int fields.";
        private const string EmptyBuildSettingsMessage = "No enabled scenes found in Build Settings.";

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!IsSupportedType(property))
            {
                DrawUnsupportedProperty(position, property, label);
                return;
            }

            List<SceneOption> options = GetEnabledSceneOptions();
            if (options.Count == 0)
            {
                DrawEmptySceneList(position, property, label);
                return;
            }

            label.text = "场景";
            DrawScenePopup(position, property, label, options);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!IsSupportedType(property) || GetEnabledSceneOptions().Count == 0)
            {
                return EditorGUI.GetPropertyHeight(property, label, true) +
                       VerticalSpacing +
                       EditorGUIUtility.singleLineHeight * 2f;
            }

            return EditorGUIUtility.singleLineHeight;
        }

        private static bool IsSupportedType(SerializedProperty property)
        {
            return property.propertyType == SerializedPropertyType.String ||
                   property.propertyType == SerializedPropertyType.Integer;
        }

        private static void DrawUnsupportedProperty(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect fieldRect = new Rect(
                position.x,
                position.y,
                position.width,
                EditorGUI.GetPropertyHeight(property, label, true));
            EditorGUI.PropertyField(fieldRect, property, label, true);

            Rect helpRect = new Rect(
                position.x,
                fieldRect.yMax + VerticalSpacing,
                position.width,
                EditorGUIUtility.singleLineHeight * 2f);
            EditorGUI.HelpBox(helpRect, UnsupportedTypeMessage, MessageType.Error);
        }

        private static void DrawEmptySceneList(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect fieldRect = new Rect(
                position.x,
                position.y,
                position.width,
                EditorGUIUtility.singleLineHeight);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.Popup(fieldRect, label, 0, new[] { new GUIContent("No enabled scenes") });
            }

            Rect helpRect = new Rect(
                position.x,
                fieldRect.yMax + VerticalSpacing,
                position.width,
                EditorGUIUtility.singleLineHeight * 2f);
            EditorGUI.HelpBox(helpRect, EmptyBuildSettingsMessage, MessageType.Warning);
        }

        private static void DrawScenePopup(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            List<SceneOption> options)
        {
            GUIContent[] labels = CreateOptionLabels(options);
            int currentIndex = FindCurrentIndex(property, options);
            int displayedIndex = currentIndex >= 0 ? currentIndex : 0;

            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            int selectedIndex = EditorGUI.Popup(position, label, displayedIndex, labels);
            if (EditorGUI.EndChangeCheck() && selectedIndex >= 0 && selectedIndex < options.Count)
            {
                ApplySelection(property, options[selectedIndex]);
            }

            EditorGUI.EndProperty();
        }

        private static GUIContent[] CreateOptionLabels(IReadOnlyList<SceneOption> options)
        {
            GUIContent[] labels = new GUIContent[options.Count];
            for (int i = 0; i < options.Count; i++)
            {
                SceneOption option = options[i];
                labels[i] = new GUIContent($"{option.BuildIndex}: {option.SceneName}", option.Path);
            }

            return labels;
        }

        private static int FindCurrentIndex(SerializedProperty property, IReadOnlyList<SceneOption> options)
        {
            for (int i = 0; i < options.Count; i++)
            {
                SceneOption option = options[i];
                if (property.propertyType == SerializedPropertyType.String &&
                    property.stringValue == option.SceneName)
                {
                    return i;
                }

                if (property.propertyType == SerializedPropertyType.Integer &&
                    property.intValue == option.BuildIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void ApplySelection(SerializedProperty property, SceneOption option)
        {
            if (property.propertyType == SerializedPropertyType.String)
            {
                property.stringValue = option.SceneName;
                return;
            }

            if (property.propertyType == SerializedPropertyType.Integer)
            {
                property.intValue = option.BuildIndex;
            }
        }

        private static List<SceneOption> GetEnabledSceneOptions()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            List<SceneOption> options = new List<SceneOption>(scenes.Length);

            for (int i = 0; i < scenes.Length; i++)
            {
                EditorBuildSettingsScene scene = scenes[i];
                if (!scene.enabled)
                {
                    continue;
                }

                int buildIndex = SceneUtility.GetBuildIndexByScenePath(scene.path);
                if (buildIndex < 0)
                {
                    continue;
                }

                options.Add(new SceneOption(
                    buildIndex,
                    Path.GetFileNameWithoutExtension(scene.path),
                    scene.path));
            }

            return options;
        }

        private readonly struct SceneOption
        {
            public SceneOption(int buildIndex, string sceneName, string path)
            {
                BuildIndex = buildIndex;
                SceneName = sceneName;
                Path = path;
            }

            public int BuildIndex { get; }
            public string SceneName { get; }
            public string Path { get; }
        }
    }
}
