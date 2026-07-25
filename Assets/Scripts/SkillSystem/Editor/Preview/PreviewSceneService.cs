#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 管理技能时间轴编辑器的预览场景和演示角色设置，并提供打开预览场景的功能。
    /// </summary>
    internal sealed class PreviewSceneService : System.IDisposable
    {
        private readonly EditorSettings settings;

        public event System.Action SettingsChanged;

        public SceneAsset PreviewScene => settings.PreviewScene;
        public GameObject PreviewActor => settings.PreviewActor;

        /// <summary>
        /// 创建并初始化 PreviewSceneService。
        /// </summary>
        public PreviewSceneService(EditorSettings settings)
        {
            this.settings = settings;
        }

        /// <summary>
        /// 释放事件订阅和该对象持有的编辑器资源。
        /// </summary>
        public void Dispose() => SettingsChanged = null;

        /// <summary>
        /// 保存固定预览场景的资产 GUID。
        /// </summary>
        public void SetPreviewScene(SceneAsset scene)
        {
            settings.SetPreviewScene(scene);
            SettingsChanged?.Invoke();
        }

        /// <summary>
        /// 保存固定演示角色的 GlobalObjectId。
        /// </summary>
        public void SetPreviewActor(GameObject actor)
        {
            settings.SetPreviewActor(actor);
            SettingsChanged?.Invoke();
        }

        /// <summary>
        /// 询问保存当前场景后打开固定预览场景。
        /// </summary>
        public bool OpenPreviewScene()
        {
            SceneAsset scene = settings.PreviewScene;
            if (scene == null)
            {
                EditorUtility.DisplayDialog("技能时间轴", "请先选择编辑器预览场景。", "确定");
                return false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            string path = AssetDatabase.GetAssetPath(scene);
            if (string.IsNullOrEmpty(path)) return false;
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            SettingsChanged?.Invoke();
            return true;
        }
    }
}
#endif
